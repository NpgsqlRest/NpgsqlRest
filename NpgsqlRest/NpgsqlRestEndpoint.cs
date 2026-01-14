using System.Collections.Frozen;
using System.Data;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using Npgsql;
using NpgsqlRest.Auth;
using NpgsqlRest.HttpClientType;
using NpgsqlRest.UploadHandlers;
using NpgsqlRest.UploadHandlers.Handlers;
using NpgsqlTypes;
using static System.Net.Mime.MediaTypeNames;
using static NpgsqlRest.ParameterParser;

namespace NpgsqlRest;

public class NpgsqlRestEndpoint(
    NpgsqlRestMetadataEntry entry,
    FrozenDictionary<string, NpgsqlRestMetadataEntry> overloads)
{
    public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        Routine routine = entry.Endpoint.Routine;
        RoutineEndpoint endpoint = entry.Endpoint;
        IRoutineSourceParameterFormatter formatter = entry.Formatter;

        string? headers = null;
        if (endpoint.RequestHeadersMode == RequestHeadersMode.Context)
        {
            SearializeHeader(Options, context, ref headers);
        }
        
        NpgsqlConnection? connection = null;
        NpgsqlTransaction? transaction = null;
        string? commandText = null;
        bool shouldDispose = true;
        bool shouldCommit = true;
        IUploadHandler? uploadHandler = null;

        var writer = PipeWriter.Create(context.Response.Body);
        try
        {
            if (endpoint.ConnectionName is not null)
            {
                // First check if there's a data source for this connection name (for multi-host support)
                if (Options.DataSources?.TryGetValue(endpoint.ConnectionName, out var namedDataSource) is true)
                {
                    connection = namedDataSource.CreateConnection();
                }
                else if (Options.ConnectionStrings?.TryGetValue(endpoint.ConnectionName, out var connectionString) is true)
                {
                    connection = new(connectionString);
                }
                else
                {
                    await ReturnErrorAsync($"Connection name {endpoint.ConnectionName} could not be found in options DataSources or ConnectionStrings dictionaries.", true, context);
                    return;
                }
            }
            else if (Options.ServiceProviderMode != ServiceProviderObject.None)
            {
                if (serviceProvider is null)
                {
                    await ReturnErrorAsync($"ServiceProvider must be provided when ServiceProviderMode is set to {Options.ServiceProviderMode}.", true, context);
                    return;
                }
                if (Options.ServiceProviderMode == ServiceProviderObject.NpgsqlDataSource)
                {
                    connection = serviceProvider.GetRequiredService<NpgsqlDataSource>().CreateConnection();
                    await connection.OpenRetryAsync(Options.ConnectionRetryOptions, cancellationToken);
                }
                else if (Options.ServiceProviderMode == ServiceProviderObject.NpgsqlConnection)
                {
                    shouldDispose = false;
                    connection = serviceProvider.GetRequiredService<NpgsqlConnection>();
                }
            }
            else
            {
                if (Options.DataSource is not null)
                {
                    connection = Options.DataSource.CreateConnection();
                }
                else
                {
                    connection = new(Options.ConnectionString);
                }
            }

            if (connection is null)
            {
                await ReturnErrorAsync("Connection did not initialize!", log: true, context);
                return;
            }

            if ( (Options.LogConnectionNoticeEvents && Logger != null) || endpoint.SseEventsPath is not null)
            {
                var currentEndpoint = endpoint;
                connection.Notice += (sender, args) =>
                {
                    if (Options.LogConnectionNoticeEvents && Logger != null)
                    {
                        NpgsqlRestLogger.LogConnectionNotice(args.Notice, Options.LogConnectionNoticeEventsMode);
                    }
                    if (currentEndpoint.SseEventsPath is not null &&
                        currentEndpoint.SseEventNoticeLevel is not null && 
                        string.Equals(args.Notice.Severity, currentEndpoint.SseEventNoticeLevel.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        NpgsqlRestSseEventSource
                            .Broadcaster
                            .Broadcast(new SseEvent(args.Notice, currentEndpoint, context.Request.Headers[Options.ExecutionIdHeaderName].FirstOrDefault()));
                    }
                };
            }
            
            if (Options.AuthenticationOptions.BasicAuth.Enabled is true || endpoint.BasicAuth?.Enabled is true)
            {
                if (Options.AuthenticationOptions.BasicAuth.ChallengeCommand is not null || endpoint.BasicAuth?.ChallengeCommand is not null)
                {
                    await OpenConnectionAsync(connection, context, endpoint);
                }
                await BasicAuthHandler.HandleAsync(context, endpoint, connection);
                if (context.Response.HasStarted is true)
                {
                    return;
                }
            }
            
            await using var command = NpgsqlRestCommand.Create(connection);

            var shouldLog = Options.LogCommands && Logger != null;
            StringBuilder? cmdLog = shouldLog ?
                new(string.Concat("-- ", context.Request.Method, " ",
                    (Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth)
                        ? string.Concat(context.Request.Scheme, "://", context.Request.Host, context.Request.Path)
                        : context.Request.GetDisplayUrl(),
                    Environment.NewLine)) :
                null;

            if (formatter.IsFormattable is false)
            {
                commandText = routine.Expression;
            }
            
            // paramsList
            bool hasNulls = false;
            int paramIndex = 0;
            JsonObject? jsonObj = null;
            Dictionary<string, JsonNode?>? bodyDict = null;
            string? body = null;
            Dictionary<string, StringValues>? queryDict = null;
            StringBuilder? cacheKeys = null;
            // Skip local upload handling if proxy will forward the upload content
            var skipUploadForProxy = endpoint.Upload is true &&
                                     endpoint.IsProxy &&
                                     Options.ProxyOptions.Enabled &&
                                     Options.ProxyOptions.ForwardUploadContent;
            uploadHandler = (endpoint.Upload is true && !skipUploadForProxy) ? endpoint.CreateUploadHandler() : null;
            int uploadMetaParamIndex = -1;
            Dictionary<string, object?>? claimsDict = null;
            List<string> customHttpTypes = Options.HttpClientOptions.Enabled ? new(routine.Parameters.Length) : null!;
            
            if (endpoint.Cached is true)
            {
                cacheKeys = new(endpoint.CachedParams?.Count ?? 0 + 1);
                cacheKeys.Append(routine.Expression);
            }

            if (endpoint.RequestParamType == RequestParamType.QueryString)
            {
                queryDict = context.Request.Query.ToDictionary();
            }
            if (endpoint.HasBodyParameter || endpoint.RequestParamType == RequestParamType.BodyJson)
            {
                context.Request.EnableBuffering();
                context.Request.Body.Position = 0;

                using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
                {
                    body = await reader.ReadToEndAsync();
                }
                if (endpoint.RequestParamType == RequestParamType.BodyJson)
                {
                    JsonNode? node = null;
                    try
                    {
                        node = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                    }
                    catch (JsonException e)
                    {
                        Logger?.CouldNotParseJson(body, context.Request.Path, e.Message);
                        node = null;
                    }
                    if (node is not null)
                    {
                        try
                        {
                            jsonObj = node?.AsObject();
                            bodyDict = jsonObj?.ToDictionary();
                        }
                        catch (Exception e)
                        {
                            Logger?.CouldNotParseJson(body, context.Request.Path, e.Message);
                            bodyDict = null;
                        }
                    }
                }
            }

            // start query string parameters
            if (endpoint.RequestParamType == RequestParamType.QueryString)
            {
                if (queryDict is null)
                {
                    shouldCommit = false;
                    uploadHandler?.OnError(connection, context, null);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.CompleteAsync();
                    return;
                }

                if (queryDict.Count != routine.ParamCount && overloads.Count > 0)
                {
                    if (overloads.TryGetValue(string.Concat(entry.Key, queryDict.Count), out var overload))
                    {
                        routine = overload.Endpoint.Routine;
                        endpoint = overload.Endpoint;
                        formatter = overload.Formatter;
                    }
                }

                for (int i = 0; i < routine.Parameters.Length; i++)
                {
                    var parameter = routine.Parameters[i].NpgsqlRestParameterMemberwiseClone();

                    if (parameter.HashOf is not null)
                    {
                        var hashValueQueryDict = queryDict.GetValueOrDefault(parameter.HashOf.ConvertedName).ToString();
                        if (string.IsNullOrEmpty(hashValueQueryDict) is true)
                        {
                            parameter.Value = DBNull.Value;
                        }
                        else
                        {
                            parameter.Value = Options.AuthenticationOptions.PasswordHasher?.HashPassword(hashValueQueryDict) as object ?? DBNull.Value;
                        }
                    }
                    if (endpoint.UseUserParameters is true)
                    {
                        if (parameter.IsFromUserClaims is true && claimsDict is null)
                        {
                            claimsDict = context.User.BuildClaimsDictionary(Options.AuthenticationOptions);
                        }
                        
                        if (parameter.IsIpAddress is true)
                        {
                            parameter.Value = context.Request.GetClientIpAddressDbParam();
                        }
                        else if (context.User?.Identity?.IsAuthenticated is true && Options.AuthenticationOptions.ParameterNameClaimsMapping.TryGetValue(parameter.ActualName, out var claimType))
                        {
                            parameter.Value = claimsDict!.GetClaimDbParam(claimType);
                        }
                        else if (context.User?.Identity?.IsAuthenticated is true && parameter.IsUserClaims is true)
                        {
                            parameter.Value = context.User.GetUserClaimsDbParam(claimsDict!);
                        }
                    }
                    if (parameter.IsUploadMetadata is true)
                    {
                        //uploadMetaParamIndex = i;
                        uploadMetaParamIndex = command.Parameters.Count; // the last one added
                        parameter.Value = DBNull.Value;
                    }

                    // body parameter
                    if (parameter.Value is null && endpoint.HasBodyParameter &&
                        (
                        string.Equals(endpoint.BodyParameterName, parameter.ConvertedName, StringComparison.Ordinal) ||
                        string.Equals(endpoint.BodyParameterName, parameter.ActualName, StringComparison.Ordinal)
                        )
                    )
                    {
                        if (body is null)
                        {
                            parameter.ParamType = ParamType.BodyParam;
                            parameter.Value = DBNull.Value;
                            hasNulls = true;

                            if (Options.ValidateParameters is not null)
                            {
                                Options.ValidateParameters(new ParameterValidationValues(
                                    context,
                                    routine,
                                    parameter));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            if (Options.ValidateParametersAsync is not null)
                            {
                                await Options.ValidateParametersAsync(new ParameterValidationValues(
                                    context,
                                    routine,
                                    parameter));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            if (endpoint.Cached is true && endpoint.CachedParams is not null)
                            {
                                if (endpoint.CachedParams.Contains(parameter.ConvertedName) || endpoint.CachedParams.Contains(parameter.ActualName))
                                {
                                    cacheKeys?.Append(NpgsqlRestParameter.GetCacheKeySeparator());
                                    cacheKeys?.Append(parameter.GetCacheStringValue());
                                }
                            }

                            if (Options.HttpClientOptions.Enabled)
                            {
                                if (parameter.TypeDescriptor.CustomType is not null)
                                {
                                    if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                                    {
                                        customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                                    }
                                }
                            }
                            command.Parameters.Add(parameter);

                            if (hasNulls is false && parameter.Value == DBNull.Value)
                            {
                                hasNulls = true;
                            }

                            if (formatter.IsFormattable is false)
                            {
                                if (formatter.RefContext)
                                {
                                    commandText = string.Concat(commandText,
                                        formatter.AppendCommandParameter(parameter, paramIndex, context));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                else
                                {
                                    commandText = string.Concat(commandText,
                                        formatter.AppendCommandParameter(parameter, paramIndex));
                                }
                            }
                            paramIndex++;
                            if (shouldLog && Options.LogCommandParameters)
                            {
                                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                    "***" :
                                    FormatParameterForLog(parameter);
                                cmdLog!.AppendLine(string.Concat(
                                    "-- $",
                                    paramIndex.ToString(),
                                    " ", parameter.TypeDescriptor.OriginalType,
                                    " = ",
                                    p));
                            }
                        }
                        else
                        {
                            StringValues bodyStringValues = body;
                            if (TryParseParameter(parameter, ref bodyStringValues, endpoint.QueryStringNullHandling))
                            {
                                parameter.ParamType = ParamType.BodyParam;
                                hasNulls = false;

                                if (Options.ValidateParameters is not null)
                                {
                                    Options.ValidateParameters(new ParameterValidationValues(
                                        context,
                                        routine,
                                        parameter));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                if (Options.ValidateParametersAsync is not null)
                                {
                                    await Options.ValidateParametersAsync(new ParameterValidationValues(
                                        context,
                                        routine,
                                        parameter));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                if (endpoint.Cached is true && endpoint.CachedParams is not null)
                                {
                                    if (endpoint.CachedParams.Contains(parameter.ConvertedName) || endpoint.CachedParams.Contains(parameter.ActualName))
                                    {
                                        cacheKeys?.Append(NpgsqlRestParameter.GetCacheKeySeparator());
                                        cacheKeys?.Append(parameter.GetCacheStringValue());
                                    }
                                }
                                
                                if (Options.HttpClientOptions.Enabled)
                                {
                                    if (parameter.TypeDescriptor.CustomType is not null)
                                    {
                                        if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                                        {
                                            customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                                        }
                                    }
                                }
                                command.Parameters.Add(parameter);
                                if (hasNulls is false && parameter.Value == DBNull.Value)
                                {
                                    hasNulls = true;
                                }

                                if (formatter.IsFormattable is false)
                                {
                                    if (formatter.RefContext)
                                    {
                                        commandText = string.Concat(commandText,
                                            formatter.AppendCommandParameter(parameter, paramIndex, context));
                                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                        {
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        commandText = string.Concat(commandText,
                                            formatter.AppendCommandParameter(parameter, paramIndex));
                                    }
                                }
                                paramIndex++;
                                if (shouldLog && Options.LogCommandParameters)
                                {
                                    var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                        "***" :
                                        FormatParameterForLog(parameter);
                                    cmdLog!.AppendLine(string.Concat(
                                        "-- $",
                                        paramIndex.ToString(),
                                        " ", parameter.TypeDescriptor.OriginalType,
                                        " = ",
                                        p));
                                }
                            }
                        }
                        continue;
                    }

                    // header parameter
                    if (parameter.Value is null &&
                        endpoint.RequestHeadersMode == RequestHeadersMode.Parameter &&
                        parameter.TypeDescriptor.HasDefault is true &&
                        (
                        string.Equals(endpoint.RequestHeadersParameterName, parameter.ConvertedName, StringComparison.Ordinal) ||
                        string.Equals(endpoint.RequestHeadersParameterName, parameter.ActualName, StringComparison.Ordinal)
                        )
                    )
                    {
                        if (queryDict.ContainsKey(parameter.ConvertedName) is false)
                        {
                            if (headers is null)
                            {
                                SearializeHeader(Options, context, ref headers);
                            }
                            parameter.ParamType = ParamType.HeaderParam;
                            parameter.Value = headers;

                            if (Options.ValidateParameters is not null)
                            {
                                Options.ValidateParameters(new ParameterValidationValues(
                                    context,
                                    routine,
                                    parameter));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            if (Options.ValidateParametersAsync is not null)
                            {
                                await Options.ValidateParametersAsync(new ParameterValidationValues(
                                    context,
                                    routine,
                                    parameter));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            if (endpoint.Cached is true && endpoint.CachedParams is not null)
                            {
                                if (endpoint.CachedParams.Contains(parameter.ConvertedName) || endpoint.CachedParams.Contains(parameter.ActualName))
                                {
                                    cacheKeys?.Append(NpgsqlRestParameter.GetCacheKeySeparator());
                                    cacheKeys?.Append(parameter.GetCacheStringValue());
                                }
                            }
                            
                            if (Options.HttpClientOptions.Enabled)
                            {
                                if (parameter.TypeDescriptor.CustomType is not null)
                                {
                                    if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                                    {
                                        customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                                    }
                                }
                            }
                            command.Parameters.Add(parameter);

                            if (hasNulls is false && parameter.Value == DBNull.Value)
                            {
                                hasNulls = true;
                            }

                            if (formatter.IsFormattable is false)
                            {
                                if (formatter.RefContext)
                                {
                                    commandText = string.Concat(commandText,
                                        formatter.AppendCommandParameter(parameter, paramIndex, context));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                else
                                {
                                    commandText = string.Concat(commandText,
                                        formatter.AppendCommandParameter(parameter, paramIndex));
                                }
                            }
                            paramIndex++;
                            if (shouldLog && Options.LogCommandParameters)
                            {
                                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                    "***" :
                                    FormatParameterForLog(parameter);
                                cmdLog!.AppendLine(string.Concat(
                                    "-- $",
                                    paramIndex.ToString(),
                                    " ", parameter.TypeDescriptor.OriginalType,
                                    " = ",
                                    p));
                            }

                            continue;
                        }
                    }

                    // path parameter - extract from RouteValues
                    if (parameter.Value is null && endpoint.HasPathParameters)
                    {
                        string? matchedPathParam = null;
                        foreach (var pathParam in endpoint.PathParameters!)
                        {
                            if (string.Equals(pathParam, parameter.ConvertedName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(pathParam, parameter.ActualName, StringComparison.OrdinalIgnoreCase))
                            {
                                matchedPathParam = pathParam;
                                break;
                            }
                        }

                        // Try to get route value using the path parameter name from the template
                        if (matchedPathParam is not null && context.Request.RouteValues.TryGetValue(matchedPathParam, out var routeValue))
                        {
                            StringValues pathStringValues = routeValue?.ToString() ?? "";
                            if (TryParseParameter(parameter, ref pathStringValues, endpoint.QueryStringNullHandling))
                            {
                                parameter.ParamType = ParamType.PathParam;
                                parameter.QueryStringValues = pathStringValues;

                                if (Options.ValidateParameters is not null)
                                {
                                    Options.ValidateParameters(new ParameterValidationValues(
                                        context,
                                        routine,
                                        parameter));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                if (Options.ValidateParametersAsync is not null)
                                {
                                    await Options.ValidateParametersAsync(new ParameterValidationValues(
                                        context,
                                        routine,
                                        parameter));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                if (endpoint.Cached is true && endpoint.CachedParams is not null)
                                {
                                    if (endpoint.CachedParams.Contains(parameter.ConvertedName) || endpoint.CachedParams.Contains(parameter.ActualName))
                                    {
                                        cacheKeys?.Append(NpgsqlRestParameter.GetCacheKeySeparator());
                                        cacheKeys?.Append(parameter.GetCacheStringValue());
                                    }
                                }

                                if (Options.HttpClientOptions.Enabled)
                                {
                                    if (parameter.TypeDescriptor.CustomType is not null)
                                    {
                                        if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                                        {
                                            customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                                        }
                                    }
                                }
                                command.Parameters.Add(parameter);

                                if (hasNulls is false && parameter.Value == DBNull.Value)
                                {
                                    hasNulls = true;
                                }

                                if (formatter.IsFormattable is false)
                                {
                                    if (formatter.RefContext)
                                    {
                                        commandText = string.Concat(commandText,
                                            formatter.AppendCommandParameter(parameter, paramIndex, context));
                                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                        {
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        commandText = string.Concat(commandText,
                                            formatter.AppendCommandParameter(parameter, paramIndex));
                                    }
                                }
                                paramIndex++;
                                if (shouldLog && Options.LogCommandParameters)
                                {
                                    var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                        "***" :
                                        FormatParameterForLog(parameter);
                                    cmdLog!.AppendLine(string.Concat(
                                        "-- $",
                                        paramIndex.ToString(),
                                        " ", parameter.TypeDescriptor.OriginalType,
                                        " = ",
                                        p));
                                }

                                continue;
                            }
                        }
                    }

                    if (queryDict.TryGetValue(parameter.ConvertedName, out var qsValue) is false)
                    {
                        if (parameter.Value is null)
                        {
                            if (Options.ValidateParameters is not null)
                            {
                                Options.ValidateParameters(new ParameterValidationValues(
                                    context,
                                    routine,
                                    parameter));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            if (Options.ValidateParametersAsync is not null)
                            {
                                await Options.ValidateParametersAsync(new ParameterValidationValues(
                                    context,
                                    routine,
                                    parameter));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            if (parameter.Value is null)
                            {
                                if (parameter.TypeDescriptor.CustomType is not null)
                                {
                                    parameter.Value = DBNull.Value;
                                }
                                else if (IsProxyResponseParameter(endpoint, parameter))
                                {
                                    // Proxy response parameter - set placeholder value and fall through to add it
                                    parameter.Value = DBNull.Value;
                                    parameter.ParamType = ParamType.QueryString;
                                }
                                else if (parameter.TypeDescriptor.HasDefault is false)
                                {
                                    shouldCommit = false;
                                    uploadHandler?.OnError(connection, context, null);
                                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                                    await context.Response.CompleteAsync();
                                    return;
                                }
                                else
                                {
                                    continue;
                                }
                            }
                        }
                    }

                    if (parameter.Value is null && TryParseParameter(parameter, ref qsValue, endpoint.QueryStringNullHandling) is false)
                    {
                        // Check if this is a proxy response parameter - it will be filled in later
                        if (IsProxyResponseParameter(endpoint, parameter))
                        {
                            parameter.Value = DBNull.Value;
                            parameter.ParamType = ParamType.QueryString;
                            // Don't skip - fall through to add the parameter
                        }
                        else if (parameter.TypeDescriptor.HasDefault is false)
                        {
                            shouldCommit = false;
                            uploadHandler?.OnError(connection, context, null);
                            context.Response.StatusCode = StatusCodes.Status404NotFound;
                            await context.Response.CompleteAsync();
                            return;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    parameter.ParamType = ParamType.QueryString;
                    parameter.QueryStringValues = qsValue;
                    if (Options.ValidateParameters is not null)
                    {
                        Options.ValidateParameters(new ParameterValidationValues(
                            context,
                            routine,
                            parameter));
                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return;
                        }
                    }
                    if (Options.ValidateParametersAsync is not null)
                    {
                        await Options.ValidateParametersAsync(new ParameterValidationValues(
                            context,
                            routine,
                            parameter));
                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return;
                        }
                    }
                    if (endpoint.Cached is true && endpoint.CachedParams is not null)
                    {
                        if (endpoint.CachedParams.Contains(parameter.ConvertedName) || endpoint.CachedParams.Contains(parameter.ActualName))
                        {
                            cacheKeys?.Append(NpgsqlRestParameter.GetCacheKeySeparator()); 
                            cacheKeys?.Append(parameter.GetCacheStringValue());
                        }
                    }

                    if (Options.HttpClientOptions.Enabled)
                    {
                        if (parameter.TypeDescriptor.CustomType is not null)
                        {
                            if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                            {
                                customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                            }
                        }
                    }
                    command.Parameters.Add(parameter);

                    if (hasNulls is false && parameter.Value == DBNull.Value)
                    {
                        hasNulls = true;
                    }

                    if (formatter.IsFormattable is false)
                    {
                        if (formatter.RefContext)
                        {
                            commandText = string.Concat(commandText,
                                formatter.AppendCommandParameter(parameter, paramIndex, context));
                            if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return;
                            }
                        }
                        else
                        {
                            commandText = string.Concat(commandText,
                                formatter.AppendCommandParameter(parameter, paramIndex));
                        }
                    }
                    paramIndex++;
                    if (shouldLog && Options.LogCommandParameters)
                    {
                        var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                            "***" :
                            FormatParameterForLog(parameter);
                        cmdLog!.AppendLine(string.Concat(
                            "-- $",
                            paramIndex.ToString(),
                            " ", parameter.TypeDescriptor.OriginalType,
                            " = ",
                            p));
                    }
                }

                // Skip query string validation for passthrough proxy endpoints - query will be forwarded as-is
                if (!(endpoint.IsProxy && Options.ProxyOptions.Enabled && !endpoint.HasProxyResponseParameters))
                {
                    foreach (var queryKey in queryDict.Keys)
                    {
                        if (routine.ParamsHash.Contains(queryKey) is false)
                        {
                            shouldCommit = false;
                            uploadHandler?.OnError(connection, context, null);
                            context.Response.StatusCode = StatusCodes.Status404NotFound;
                            await context.Response.CompleteAsync();
                            return;
                        }
                    }
                }
            } // end of query string parameters
            // start json body parameters
            else if (endpoint.RequestParamType == RequestParamType.BodyJson)
            {
                if (bodyDict is null)
                {
                    shouldCommit = false;
                    uploadHandler?.OnError(connection, context, null);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.CompleteAsync();
                    return;
                }

                // Account for path parameters when counting body parameters
                var pathParamCount = endpoint.PathParameters?.Length ?? 0;
                var expectedBodyParamCount = routine.ParamCount - pathParamCount;
                if (bodyDict.Count != expectedBodyParamCount && overloads.Count > 0)
                {
                    if (overloads.TryGetValue(string.Concat(entry.Key, bodyDict.Count + pathParamCount), out var overload))
                    {
                        routine = overload.Endpoint.Routine;
                        endpoint = overload.Endpoint;
                        formatter = overload.Formatter;
                    }
                }

                for (int i = 0; i < routine.Parameters.Length; i++)
                {
                    var parameter = routine.Parameters[i].NpgsqlRestParameterMemberwiseClone();

                    if (parameter.HashOf is not null)
                    {
                        var hashValueBodyDict = bodyDict.GetValueOrDefault(parameter.HashOf.ConvertedName)?.ToString();
                        if (string.IsNullOrEmpty(hashValueBodyDict) is true)
                        {
                            parameter.Value = DBNull.Value;
                        }
                        else
                        {
                            parameter.Value = Options.AuthenticationOptions.PasswordHasher?.HashPassword(hashValueBodyDict) as object ?? DBNull.Value;
                        }
                    }
                    if (endpoint.UseUserParameters is true)
                    {
                        if (parameter.IsFromUserClaims is true && claimsDict is null)
                        {
                            claimsDict = context.User.BuildClaimsDictionary(Options.AuthenticationOptions);
                        }
                        if (parameter.IsIpAddress is true)
                        {
                            parameter.Value = context.Request.GetClientIpAddressDbParam();
                        }
                        else if (context.User?.Identity?.IsAuthenticated is true && Options.AuthenticationOptions.ParameterNameClaimsMapping.TryGetValue(parameter.ActualName, out var claimType))
                        {
                            parameter.Value = claimsDict!.GetClaimDbParam(claimType);
                        }
                        else if (context.User?.Identity?.IsAuthenticated is true && parameter.IsUserClaims is true)
                        {
                            parameter.Value = context.User!.GetUserClaimsDbParam(claimsDict!);
                        }
                    }
                    if (parameter.IsUploadMetadata is true)
                    {
                        uploadMetaParamIndex = command.Parameters.Count; // the last one added
                        parameter.Value = DBNull.Value;
                    }

                    // header parameter
                    if (parameter.Value is null &&
                        endpoint.RequestHeadersMode == RequestHeadersMode.Parameter &&
                        parameter.TypeDescriptor.HasDefault is true &&
                    (
                        string.Equals(endpoint.RequestHeadersParameterName, parameter.ConvertedName, StringComparison.Ordinal) ||
                        string.Equals(endpoint.RequestHeadersParameterName, parameter.ActualName, StringComparison.Ordinal)
                        )
                    )
                    {
                        if (bodyDict.ContainsKey(parameter.ConvertedName) is false)
                        {
                            if (headers is null)
                            {
                                SearializeHeader(Options, context, ref headers);
                            }
                            parameter.ParamType = ParamType.HeaderParam;
                            parameter.Value = headers;

                            if (Options.ValidateParameters is not null)
                            {
                                Options.ValidateParameters(new ParameterValidationValues(
                                    context,
                                    routine,
                                    parameter));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            if (Options.ValidateParametersAsync is not null)
                            {
                                await Options.ValidateParametersAsync(new ParameterValidationValues(
                                    context,
                                    routine,
                                    parameter));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            if (endpoint.Cached is true && endpoint.CachedParams is not null)
                            {
                                if (endpoint.CachedParams.Contains(parameter.ConvertedName) || endpoint.CachedParams.Contains(parameter.ActualName))
                                {
                                    cacheKeys?.Append(NpgsqlRestParameter.GetCacheKeySeparator());
                                    cacheKeys?.Append(parameter.GetCacheStringValue());
                                }
                            }

                            if (Options.HttpClientOptions.Enabled)
                            {
                                if (parameter.TypeDescriptor.CustomType is not null)
                                {
                                    if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                                    {
                                        customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                                    }
                                }
                            }
                            command.Parameters.Add(parameter);

                            if (hasNulls is false && parameter.Value == DBNull.Value)
                            {
                                hasNulls = true;
                            }

                            if (formatter.IsFormattable is false)
                            {
                                if (formatter.RefContext)
                                {
                                    commandText = string.Concat(commandText,
                                        formatter.AppendCommandParameter(parameter, paramIndex, context));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                else
                                {
                                    commandText = string.Concat(commandText,
                                        formatter.AppendCommandParameter(parameter, paramIndex));
                                }
                            }
                            paramIndex++;
                            if (shouldLog && Options.LogCommandParameters)
                            {
                                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                    "***" :
                                    FormatParameterForLog(parameter);
                                cmdLog!.AppendLine(string.Concat(
                                    "-- $",
                                    paramIndex.ToString(),
                                    " ", parameter.TypeDescriptor.OriginalType,
                                    " = ",
                                    p));
                            }

                            continue;
                        }
                    }

                    // path parameter - extract from RouteValues (for JSON body mode)
                    if (parameter.Value is null && endpoint.HasPathParameters)
                    {
                        string? matchedPathParam = null;
                        foreach (var pathParam in endpoint.PathParameters!)
                        {
                            if (string.Equals(pathParam, parameter.ConvertedName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(pathParam, parameter.ActualName, StringComparison.OrdinalIgnoreCase))
                            {
                                matchedPathParam = pathParam;
                                break;
                            }
                        }

                        // Try to get route value using the path parameter name from the template
                        if (matchedPathParam is not null && context.Request.RouteValues.TryGetValue(matchedPathParam, out var routeValue))
                        {
                            StringValues pathStringValues = routeValue?.ToString() ?? "";
                            if (TryParseParameter(parameter, ref pathStringValues, endpoint.QueryStringNullHandling))
                            {
                                parameter.ParamType = ParamType.PathParam;
                                parameter.QueryStringValues = pathStringValues;

                                if (Options.ValidateParameters is not null)
                                {
                                    Options.ValidateParameters(new ParameterValidationValues(
                                        context,
                                        routine,
                                        parameter));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                if (Options.ValidateParametersAsync is not null)
                                {
                                    await Options.ValidateParametersAsync(new ParameterValidationValues(
                                        context,
                                        routine,
                                        parameter));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                if (endpoint.Cached is true && endpoint.CachedParams is not null)
                                {
                                    if (endpoint.CachedParams.Contains(parameter.ConvertedName) || endpoint.CachedParams.Contains(parameter.ActualName))
                                    {
                                        cacheKeys?.Append(NpgsqlRestParameter.GetCacheKeySeparator());
                                        cacheKeys?.Append(parameter.GetCacheStringValue());
                                    }
                                }

                                if (Options.HttpClientOptions.Enabled)
                                {
                                    if (parameter.TypeDescriptor.CustomType is not null)
                                    {
                                        if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                                        {
                                            customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                                        }
                                    }
                                }
                                command.Parameters.Add(parameter);

                                if (hasNulls is false && parameter.Value == DBNull.Value)
                                {
                                    hasNulls = true;
                                }

                                if (formatter.IsFormattable is false)
                                {
                                    if (formatter.RefContext)
                                    {
                                        commandText = string.Concat(commandText,
                                            formatter.AppendCommandParameter(parameter, paramIndex, context));
                                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                        {
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        commandText = string.Concat(commandText,
                                            formatter.AppendCommandParameter(parameter, paramIndex));
                                    }
                                }
                                paramIndex++;
                                if (shouldLog && Options.LogCommandParameters)
                                {
                                    var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                        "***" :
                                        FormatParameterForLog(parameter);
                                    cmdLog!.AppendLine(string.Concat(
                                        "-- $",
                                        paramIndex.ToString(),
                                        " ", parameter.TypeDescriptor.OriginalType,
                                        " = ",
                                        p));
                                }

                                continue;
                            }
                        }
                    }

                    if (bodyDict.TryGetValue(parameter.ConvertedName, out var value) is false)
                    {
                        if (parameter.Value is null)
                        {
                            if (Options.ValidateParameters is not null)
                            {
                                Options.ValidateParameters(new ParameterValidationValues(
                                    context,
                                    routine,
                                    parameter));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            if (Options.ValidateParametersAsync is not null)
                            {
                                await Options.ValidateParametersAsync(new ParameterValidationValues(
                                    context,
                                    routine,
                                    parameter));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            if (parameter.Value is null)
                            {
                                if (parameter.TypeDescriptor.CustomType is not null)
                                {
                                    parameter.Value = DBNull.Value;
                                }
                                else if (IsProxyResponseParameter(endpoint, parameter))
                                {
                                    // Proxy response parameter - set placeholder value and fall through to add it
                                    parameter.Value = DBNull.Value;
                                    parameter.ParamType = ParamType.BodyJson;
                                }
                                else if (parameter.TypeDescriptor.HasDefault is false)
                                {
                                    shouldCommit = false;
                                    uploadHandler?.OnError(connection, context, null);
                                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                                    await context.Response.CompleteAsync();
                                    return;
                                }
                                else
                                {
                                    continue;
                                }
                            }
                        }
                    }

                    if (parameter.Value is null && TryParseParameter(parameter, value) is false)
                    {
                        // Check if this is a proxy response parameter - it will be filled in later
                        if (IsProxyResponseParameter(endpoint, parameter))
                        {
                            parameter.Value = DBNull.Value;
                            parameter.ParamType = ParamType.BodyJson;
                            // Don't skip - fall through to add the parameter
                        }
                        else if (parameter.TypeDescriptor.HasDefault is false)
                        {
                            shouldCommit = false;
                            uploadHandler?.OnError(connection, context, null);
                            context.Response.StatusCode = StatusCodes.Status404NotFound;
                            await context.Response.CompleteAsync();
                            return;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    parameter.ParamType = ParamType.BodyJson;
                    parameter.JsonBodyNode = value;
                    if (Options.ValidateParameters is not null)
                    {
                        Options.ValidateParameters(new ParameterValidationValues(
                            context,
                            routine,
                            parameter));
                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return;
                        }
                    }
                    if (Options.ValidateParametersAsync is not null)
                    {
                        await Options.ValidateParametersAsync(new ParameterValidationValues(
                            context,
                            routine,
                            parameter));
                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return;
                        }
                    }
                    if (endpoint.Cached is true && endpoint.CachedParams is not null)
                    {
                        if (endpoint.CachedParams.Contains(parameter.ConvertedName) || endpoint.CachedParams.Contains(parameter.ActualName))
                        {
                            cacheKeys?.Append(NpgsqlRestParameter.GetCacheKeySeparator());
                            cacheKeys?.Append(parameter.GetCacheStringValue());
                        }
                    }

                    if (Options.HttpClientOptions.Enabled)
                    {
                        if (parameter.TypeDescriptor.CustomType is not null)
                        {
                            if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                            {
                                customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                            }
                        }
                    }
                    command.Parameters.Add(parameter);

                    if (hasNulls is false && parameter.Value == DBNull.Value)
                    {
                        hasNulls = true;
                    }

                    if (formatter.IsFormattable is false)
                    {
                        if (formatter.RefContext)
                        {
                            commandText = string.Concat(commandText,
                                formatter.AppendCommandParameter(parameter, paramIndex, context));
                            if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return;
                            }
                        }
                        else
                        {
                            commandText = string.Concat(commandText,
                                formatter.AppendCommandParameter(parameter, paramIndex));
                        }
                    }
                    paramIndex++;
                    if (shouldLog && Options.LogCommandParameters)
                    {
                        var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                            "***" :
                            FormatParameterForLog(parameter);
                        cmdLog!.AppendLine(string.Concat(
                            "-- $",
                            paramIndex.ToString(),
                            " ", parameter.TypeDescriptor.OriginalType,
                            " = ",
                            p));
                    }
                }

                // Skip body validation for passthrough proxy endpoints - body will be forwarded as-is
                if (!(endpoint.IsProxy && Options.ProxyOptions.Enabled && !endpoint.HasProxyResponseParameters))
                {
                    foreach (var bodyKey in bodyDict.Keys)
                    {
                        if (routine.ParamsHash.Contains(bodyKey) is false)
                        {
                            shouldCommit = false;
                            uploadHandler?.OnError(connection, context, null);
                            context.Response.StatusCode = StatusCodes.Status404NotFound;
                            await context.Response.CompleteAsync();
                            return;
                        }
                    }
                }
            } // end of json body parameters

            if (hasNulls && routine.IsStrict)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                await context.Response.CompleteAsync();
                return;
            }

            // parameters are ready !!!

            // Validate parameters before any further processing (authorization, proxy, etc.)
            if (endpoint.ParameterValidations is not null)
            {
                if (!await ValidateParametersAsync(command.Parameters, endpoint, context))
                {
                    return;
                }
            }

            // authorization check
            if (endpoint.Login is false)
            {
                if ((endpoint.RequiresAuthorization || endpoint.AuthorizeRoles is not null) && context.User?.Identity?.IsAuthenticated is false)
                {
                    await Results.Problem(
                        type: null,
                        statusCode: (int)HttpStatusCode.Unauthorized,
                        title: "Unauthorized",
                        detail: null).ExecuteAsync(context);
                    return;
                }

                if (endpoint.AuthorizeRoles is not null)
                {
                    bool ok = false;
                    foreach (var claim in context.User?.Claims ?? [])
                    {
                        if (string.Equals(claim.Type, Options.AuthenticationOptions.DefaultRoleClaimType, StringComparison.Ordinal))
                        {
                            if (endpoint.AuthorizeRoles.Contains(claim.Value) is true)
                            {
                                ok = true;
                                break;
                            }
                        }
                    }
                    if (ok is false)
                    {
                        await Results.Problem(
                            type: null,
                            statusCode: (int)HttpStatusCode.Forbidden,
                            title: "Forbidden",
                            detail: null).ExecuteAsync(context);
                        return;
                    }
                }
            }

            if (formatter.IsFormattable is true)
            {
                if (formatter.RefContext)
                {
                    commandText = formatter.FormatCommand(routine, command.Parameters, context);
                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                    {
                        return;
                    }
                }
                else
                {
                    commandText = formatter.FormatCommand(routine, command.Parameters);
                }
            }
            if (formatter.IsFormattable is false)
            {
                if (formatter.RefContext)
                {
                    commandText = string.Concat(commandText, formatter.AppendEmpty(context));
                    if (formatter.IsFormattable)
                    {
                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return;
                        }
                    }
                }
                else
                {
                    commandText = string.Concat(commandText, formatter.AppendEmpty());
                }
            }

            if (commandText is null)
            {
                shouldCommit = false;
                uploadHandler?.OnError(connection, context, null);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.CompleteAsync();
                return;
            }

            Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>>? lookup = null;
            if (endpoint.HeadersNeedParsing is true || endpoint.CustomParamsNeedParsing || HttpClientTypes.NeedsParsing)
            {
                Dictionary<string, string> replacements = new(command.Parameters.Count * 2);
                for (var i = 0; i < command.Parameters.Count; i++)
                {
                    var value = command.Parameters[i].Value == DBNull.Value ? "" : command.Parameters[i].Value?.ToString() ?? "";
                    var param = (NpgsqlRestParameter)command.Parameters[i];
                    if (!string.IsNullOrEmpty(param.ActualName))
                    {
                        replacements[param.ActualName] = value;
                    }
                    replacements[param.ConvertedName] = value;
                    if (param.TypeDescriptor.CustomTypeName is not null)
                    {
                        replacements[param.TypeDescriptor.CustomTypeName] = value;
                    }
                }
                lookup = replacements.GetAlternateLookup<ReadOnlySpan<char>>();
            }

            if (endpoint.HeadersNeedParsing is true || endpoint.CustomParamsNeedParsing)
            {
                if (endpoint.CustomParamsNeedParsing && endpoint.CustomParameters is not null)
                {
                    foreach (var (key, value) in endpoint.CustomParameters)
                    {
                        endpoint.CustomParameters[key] = Formatter.FormatString(value, lookup!.Value).ToString();
                    }
                }
            }

            if (endpoint.ResponseContentType is not null || endpoint.ResponseHeaders.Count > 0)
            {
                if (endpoint.HeadersNeedParsing is true)
                {
                    if (endpoint.ResponseContentType is not null)
                    {
                        context.Response.ContentType = Formatter.FormatString(endpoint.ResponseContentType, lookup!.Value).ToString();
                    }
                    if (endpoint.ResponseHeaders.Count > 0)
                    {
                        foreach (var (headerKey, headerValue) in endpoint.ResponseHeaders)
                        {
                            context.Response.Headers.Append(headerKey, Formatter.FormatString(headerValue.ToString(), lookup!.Value).ToString());
                        }
                    }
                }
                else
                {
                    if (endpoint.ResponseContentType is not null)
                    {
                        context.Response.ContentType = endpoint.ResponseContentType;
                    }
                    if (endpoint.ResponseHeaders.Count > 0)
                    {
                        foreach (var (headerKey, headerValue) in endpoint.ResponseHeaders)
                        {
                            context.Response.Headers.Append(headerKey, headerValue);
                        }
                    }
                }
            }

            if (Options.HttpClientOptions.Enabled && customHttpTypes.Count > 0)
            {
                await HttpClientTypeHandler.InvokeAllAsync(customHttpTypes, lookup, command.Parameters, cancellationToken);
            }

            // Handle reverse proxy endpoints
            if (endpoint.IsProxy && Options.ProxyOptions.Enabled)
            {
                // Check cache for passthrough proxy endpoints (those without proxy response parameters)
                bool isPassthroughProxy = !endpoint.HasProxyResponseParameters;
                if (isPassthroughProxy &&
                    endpoint.Cached is true &&
                    Options.CacheOptions.DefaultRoutineCache is not null)
                {
                    if (Options.CacheOptions.DefaultRoutineCache.Get(endpoint, cacheKeys?.ToString()!, out var cachedProxyResponse))
                    {
                        // Cache hit - return cached proxy response
                        if (cachedProxyResponse is Proxy.ProxyResponse cached)
                        {
                            if (shouldLog)
                            {
                                cmdLog?.AppendLine("/* proxy response from cache */");
                                NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", commandText);
                            }
                            await Proxy.ProxyRequestHandler.WriteResponseAsync(context, cached, Options.ProxyOptions);
                            return;
                        }
                    }
                }

                // Build user context headers if UserContext is enabled
                Dictionary<string, string>? userContextHeaders = null;
                if (endpoint.UserContext is true)
                {
                    userContextHeaders = [];
                    claimsDict ??= context.User.BuildClaimsDictionary(Options.AuthenticationOptions);

                    // Add IP address header
                    if (Options.AuthenticationOptions.IpAddressContextKey is not null)
                    {
                        var ipAddress = context.Request.GetClientIpAddress();
                        if (!string.IsNullOrEmpty(ipAddress))
                        {
                            userContextHeaders[Options.AuthenticationOptions.IpAddressContextKey] = ipAddress;
                        }
                    }

                    if (context.User?.Identity?.IsAuthenticated is true)
                    {
                        // Add claims JSON header
                        if (Options.AuthenticationOptions.ClaimsJsonContextKey is not null)
                        {
                            var claimsJson = context.User!.GetUserClaimsDbParam(claimsDict!);
                            if (claimsJson is string claimsJsonStr)
                            {
                                userContextHeaders[Options.AuthenticationOptions.ClaimsJsonContextKey] = claimsJsonStr;
                            }
                        }

                        // Add individual claim headers from ContextKeyClaimsMapping
                        foreach (var mapping in Options.AuthenticationOptions.ContextKeyClaimsMapping)
                        {
                            var claimValue = claimsDict!.GetClaimDbContextParam(mapping.Value);
                            if (claimValue is string claimValueStr)
                            {
                                userContextHeaders[mapping.Key] = claimValueStr;
                            }
                        }
                    }
                }

                var proxyResponse = await Proxy.ProxyRequestHandler.InvokeAsync(context, endpoint, body, command.Parameters, userContextHeaders, cancellationToken);

                if (endpoint.HasProxyResponseParameters)
                {
                    // Map proxy response to parameters and continue with routine execution
                    MapProxyResponseToParameters(proxyResponse, command.Parameters, endpoint);
                }
                else
                {
                    // No proxy parameters - return proxy response directly without invoking PostgreSQL
                    // Cache the proxy response if caching is enabled
                    if (endpoint.Cached is true && Options.CacheOptions.DefaultRoutineCache is not null)
                    {
                        Options.CacheOptions.DefaultRoutineCache.AddOrUpdate(endpoint, cacheKeys?.ToString()!, proxyResponse);
                    }
                    await Proxy.ProxyRequestHandler.WriteResponseAsync(context, proxyResponse, Options.ProxyOptions);
                    return;
                }
            }

            // Set user context BEFORE upload so that upload row commands can access user claims
            if (
                (endpoint.RequestHeadersMode == RequestHeadersMode.Context && headers is not null && Options.RequestHeadersContextKey is not null)
                ||
                (endpoint.UserContext is true && Options.AuthenticationOptions.IpAddressContextKey is not null)
                ||
                (endpoint.UserContext is true && context.User?.Identity?.IsAuthenticated is true &&
                    (Options.AuthenticationOptions.ClaimsJsonContextKey is not null || Options.AuthenticationOptions.ContextKeyClaimsMapping.Count > 0)
                    )
                )
            {
                if (connection.State != ConnectionState.Open)
                {
                    if (Options.BeforeConnectionOpen is not null)
                    {
                        Options.BeforeConnectionOpen(connection, endpoint, context);
                    }
                    await connection.OpenRetryAsync(Options.ConnectionRetryOptions, cancellationToken);
                }
                await using var batch = NpgsqlRestBatch.Create(connection);

                if (endpoint.RequestHeadersMode == RequestHeadersMode.Context && headers is not null && Options.RequestHeadersContextKey is not null)
                {
                    var cmd = new NpgsqlBatchCommand(Consts.SetContext);
                    cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.RequestHeadersContextKey));
                    cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(headers));
                    batch.BatchCommands.Add(cmd);
                }

                if (endpoint.UserContext is true)
                {
                    claimsDict ??= context.User.BuildClaimsDictionary(Options.AuthenticationOptions);

                    if (Options.AuthenticationOptions.IpAddressContextKey is not null)
                    {
                        var cmd = new NpgsqlBatchCommand(Consts.SetContext);
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.AuthenticationOptions.IpAddressContextKey));
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(context.Request.GetClientIpAddressDbParam()));
                        batch.BatchCommands.Add(cmd);
                    }
                    if (context.User?.Identity?.IsAuthenticated is true && Options.AuthenticationOptions.ClaimsJsonContextKey is not null)
                    {
                        var cmd = new NpgsqlBatchCommand(Consts.SetContext);
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.AuthenticationOptions.ClaimsJsonContextKey));
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(context.User!.GetUserClaimsDbParam(claimsDict!)));
                        batch.BatchCommands.Add(cmd);
                    }
                    if (context.User?.Identity?.IsAuthenticated is true)
                    {
                        foreach (var mapping in Options.AuthenticationOptions.ContextKeyClaimsMapping)
                        {
                            var cmd = new NpgsqlBatchCommand(Consts.SetContext);
                            cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(mapping.Key));
                            cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(claimsDict!.GetClaimDbContextParam(mapping.Value)));
                            batch.BatchCommands.Add(cmd);
                        }
                    }
                }

                await batch.ExecuteBatchWithRetryAsync(endpoint.RetryStrategy, cancellationToken);
            }

            object? uploadMetadata = null;
            if (endpoint.Upload is true && uploadHandler is not null)
            {
                if (connection.State != ConnectionState.Open)
                {
                    if (Options.BeforeConnectionOpen is not null)
                    {
                        Options.BeforeConnectionOpen(connection, endpoint, context);
                    }
                    await connection.OpenRetryAsync(Options.ConnectionRetryOptions, cancellationToken);
                }
                if (uploadHandler.RequiresTransaction is true)
                {
                    transaction = await connection.BeginTransactionAsync();
                }
                uploadMetadata = await uploadHandler.UploadAsync(connection, context, endpoint.CustomParameters);
                uploadMetadata ??= DBNull.Value;
                if (uploadMetaParamIndex > -1)
                {
                    command.Parameters[uploadMetaParamIndex].Value = uploadMetadata;
                }
            }

            // Set upload metadata context AFTER upload (since it depends on uploadMetadata)
            if (Options.UploadOptions.UseDefaultUploadMetadataContextKey &&
                Options.UploadOptions.DefaultUploadMetadataContextKey is not null &&
                endpoint.Upload is true &&
                uploadHandler is not null &&
                uploadMetadata is not null)
            {
                if (connection.State != ConnectionState.Open)
                {
                    if (Options.BeforeConnectionOpen is not null)
                    {
                        Options.BeforeConnectionOpen(connection, endpoint, context);
                    }
                    await connection.OpenRetryAsync(Options.ConnectionRetryOptions, cancellationToken);
                }
                await using var batch = NpgsqlRestBatch.Create(connection);
                var cmd = new NpgsqlBatchCommand(Consts.SetContext);
                cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.UploadOptions.DefaultUploadMetadataContextKey));
                cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(uploadMetadata));
                batch.BatchCommands.Add(cmd);
                await batch.ExecuteBatchWithRetryAsync(endpoint.RetryStrategy, cancellationToken);
            }
            
            if (endpoint.Login is true)
            {
                if (await PrepareCommand(connection, command, commandText, context, endpoint, false) is false)
                {
                    return;
                }
                if (shouldLog)
                {
                    NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                }
                await LoginHandler.HandleAsync(
                    command,
                    context,
                    endpoint.RetryStrategy,
                    tracePath: context.Request.Path.ToString(),
                    performHashVerification: true,
                    assignUserPrincipalToContext: false);
                
                if (context.Response.HasStarted is true ||
                    Options.AuthenticationOptions.SerializeAuthEndpointsResponse is false)
                {
                    return;
                }
            }

            if (endpoint.Logout is true)
            {
                if (await PrepareCommand(connection, command, commandText, context, endpoint, true) is false)
                {
                    return;
                }
                if (shouldLog)
                {
                    NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                }
                await LogoutHandler.HandleAsync(command, endpoint, context);
                return;
            }

            // Handle cache invalidation endpoint
            if (endpoint.InvalidateCache is true && Options.CacheOptions.DefaultRoutineCache is not null && cacheKeys is not null)
            {
                var cacheKey = cacheKeys.ToString();
                var removed = Options.CacheOptions.DefaultRoutineCache.Remove(cacheKey);
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                await context.Response.WriteAsync(removed ? "{\"invalidated\":true}" : "{\"invalidated\":false}", cancellationToken);
                if (shouldLog)
                {
                    cmdLog?.AppendLine($"/* cache invalidation: {(removed ? "removed" : "not found")} */");
                    NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", $"INVALIDATE CACHE KEY: {cacheKey}");
                }
                return;
            }

            if (routine.IsVoid)
            {
                if (await PrepareCommand(connection, command, commandText, context, endpoint, true) is false)
                {
                    return;
                }
                if (shouldLog)
                {
                    NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                }
                await command.ExecuteNonQueryWithRetryAsync(
                    endpoint.RetryStrategy, 
                    cancellationToken, 
                    errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }
            else // end if (routine.IsVoid)
            {
                if (routine.ReturnsSet == false && routine.ColumnCount == 1 && routine.ReturnsRecordType is false)
                {
                    TypeDescriptor descriptor = routine.ColumnsTypeDescriptor[0];

                    object? valueResult;
                    if (Options.CacheOptions.DefaultRoutineCache is not null && endpoint.Cached is true)
                    {
                        if (Options.CacheOptions.DefaultRoutineCache.Get(endpoint, cacheKeys?.ToString()!, out valueResult) is false)
                        {
                            if (await PrepareCommand(connection, command, commandText, context, endpoint, true) is false)
                            {
                                return;
                            }
                            
                            await using var reader = await command.ExecuteReaderWithRetryAsync(
                                CommandBehavior.SequentialAccess,
                                endpoint.RetryStrategy,
                                cancellationToken,
                                errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                            if (shouldLog)
                            {
                                NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                            }
                            if (await reader.ReadAsync())
                            {
                                valueResult = descriptor.IsBinary ? reader.GetFieldValue<byte[]>(0) : reader.GetValue(0) as string;
                                Options.CacheOptions.DefaultRoutineCache.AddOrUpdate(endpoint, cacheKeys?.ToString()!, valueResult);
                            }
                            else
                            {
                                Logger?.CouldNotReadCommand(command.CommandText, context.Request.Method, context.Request.Path);
                                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                                return;
                            }
                        }
                        else
                        {
                            if (shouldLog)
                            {
                                cmdLog?.AppendLine("/* from cache */");
                                NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", commandText);
                            }
                        }
                    }
                    else
                    { 
                        if (await PrepareCommand(connection, command, commandText, context, endpoint, true) is false)
                        {
                            return;
                        }
                        await using var reader = await command.ExecuteReaderWithRetryAsync(
                            CommandBehavior.SequentialAccess,
                            endpoint.RetryStrategy,
                            cancellationToken,
                            errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                        if (shouldLog)
                        {
                            NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                        }
                        if (await reader.ReadAsync())
                        {
                            valueResult = descriptor.IsBinary ? reader.GetFieldValue<byte[]>(0) : reader.GetValue(0) as string;
                        }
                        else
                        {
                            Logger?.CouldNotReadCommand(command.CommandText, context.Request.Method, context.Request.Path);
                            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                            return;
                        }
                    }

                    if (context.Response.ContentType is null)
                    {
                        if (descriptor.IsBinary)
                        {
                            context.Response.ContentType = Application.Octet;
                        }
                        else if (descriptor.IsJson || descriptor.IsArray)
                        {
                            context.Response.ContentType = Application.Json;
                        }
                        else
                        {
                            context.Response.ContentType = Text.Plain;
                        }
                    }

                    // if raw
                    if (endpoint.Raw)
                    {
                        if (descriptor.IsBinary)
                        {
                            await writer.WriteAsync(valueResult as byte[]);
                            await writer.FlushAsync();
                        }
                        else
                        {
                            var span = (valueResult as string).AsSpan();
                            writer.Advance(Encoding.UTF8.GetBytes(span, writer.GetSpan(Encoding.UTF8.GetMaxByteCount(span.Length))));
                        }
                    }
                    else
                    {
                        if (valueResult is null)
                        {
                            if (endpoint.TextResponseNullHandling == TextResponseNullHandling.NullLiteral)
                            {
                                writer.Advance(Encoding.UTF8.GetBytes(Consts.Null.AsSpan(), writer.GetSpan(Encoding.UTF8.GetMaxByteCount(Consts.Null.Length))));
                            }
                            else if (endpoint.TextResponseNullHandling == TextResponseNullHandling.NoContent)
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                            }
                            // else OK empty string
                            return;
                        }
                        if (descriptor.IsBinary)
                        {
                            await writer.WriteAsync(valueResult as byte[]);
                            await writer.FlushAsync();
                        }
                        else
                        {
                            var span = (descriptor.IsArray && valueResult is not null) ?
                                PgArrayToJsonArray((valueResult as string).AsSpan(), descriptor) : (valueResult as string).AsSpan();
                            writer.Advance(Encoding.UTF8.GetBytes(span, writer.GetSpan(Encoding.UTF8.GetMaxByteCount(span.Length))));
                        }
                    }
                    return;

                }
                else // end if (routine.ReturnsRecord == false)
                {
                    var binary = routine.ColumnsTypeDescriptor.Length == 1 && routine.ColumnsTypeDescriptor[0].IsBinary;

                    // Check cache for records/sets (but not for binary or raw mode)
                    var canCacheRecordsAndSets = Options.CacheOptions.DefaultRoutineCache is not null
                        && endpoint.Cached is true
                        && binary is false
                        && endpoint.Raw is false;

                    if (canCacheRecordsAndSets)
                    {
                        if (Options.CacheOptions.DefaultRoutineCache!.Get(endpoint, cacheKeys?.ToString()!, out var cachedResult))
                        {
                            if (shouldLog)
                            {
                                cmdLog?.AppendLine("/* from cache */");
                                NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", commandText ?? "");
                            }

                            if (context.Response.ContentType is null)
                            {
                                context.Response.ContentType = Application.Json;
                            }

                            var cachedSpan = (cachedResult as string ?? "").AsSpan();
                            writer.Advance(Encoding.UTF8.GetBytes(cachedSpan, writer.GetSpan(Encoding.UTF8.GetMaxByteCount(cachedSpan.Length))));
                            return;
                        }
                    }

                    if (await PrepareCommand(connection, command, commandText, context, endpoint, true) is false)
                    {
                        return;
                    }
                    await using var reader = await command.ExecuteReaderWithRetryAsync(
                        CommandBehavior.SequentialAccess,
                        endpoint.RetryStrategy,
                        cancellationToken,
                        errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                    if (shouldLog)
                    {
                        NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                    }
                    if (context.Response.ContentType is null)
                    {
                        if (binary is true)
                        {
                            context.Response.ContentType = Application.Octet;
                        }
                        else
                        {
                            context.Response.ContentType = Application.Json;
                        }
                    }

                    // For caching, we need to buffer the entire response
                    StringBuilder? cacheBuffer = canCacheRecordsAndSets ? new() : null;
                    var maxCacheableRows = Options.CacheOptions.MaxCacheableRows;
                    var shouldCache = canCacheRecordsAndSets;

                    if (routine.ReturnsSet && endpoint.Raw is false && binary is false)
                    {
                        writer.Advance(Encoding.UTF8.GetBytes(Consts.OpenBracket.ToString().AsSpan(), writer.GetSpan(Encoding.UTF8.GetMaxByteCount(1))));
                        if (shouldCache)
                        {
                            cacheBuffer!.Append(Consts.OpenBracket);
                        }
                    }

                    bool first = true;
                    var routineReturnRecordCount = routine.ColumnCount;

                    StringBuilder row = new();
                    ulong rowCount = 0;

                    // Precompute nested JSON composite column mapping
                    // Maps column index to: (compositeColumnName, fieldName, isFirstField, isLastField, fieldCount)
                    Dictionary<int, (string CompositeColumnName, string FieldName, bool IsFirstField, bool IsLastField, int FieldCount)>? nestedJsonColumnMap = null;
                    if (endpoint.NestedJsonForCompositeTypes == true && routine.CompositeColumnInfo is not null)
                    {
                        nestedJsonColumnMap = new();
                        foreach (var (_, compositeInfo) in routine.CompositeColumnInfo)
                        {
                            var indices = compositeInfo.ExpandedColumnIndices;
                            var fieldCount = indices.Length;
                            for (int fieldIdx = 0; fieldIdx < fieldCount; fieldIdx++)
                            {
                                var colIdx = indices[fieldIdx];
                                nestedJsonColumnMap[colIdx] = (
                                    compositeInfo.ConvertedColumnName,
                                    compositeInfo.FieldNames[fieldIdx],
                                    fieldIdx == 0,
                                    fieldIdx == indices.Length - 1,
                                    fieldCount
                                );
                            }
                        }
                    }

                    // Buffer for composite fields to detect all-NULL composites
                    StringBuilder? compositeBuffer = nestedJsonColumnMap is not null ? new() : null;
                    bool compositeHasNonNullValue = false;
                    string? currentCompositeName = null;

                    if (endpoint.Raw is true && endpoint.RawColumnNames is true && binary is false)
                    {
                        StringBuilder columns = new();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            if (endpoint.RawValueSeparator is not null && i > 0)
                            {
                                columns.Append(endpoint.RawValueSeparator);
                            }
                            columns.Append(QuoteText(reader.GetName(i).AsSpan()));
                        }
                        if (endpoint.RawNewLineSeparator is not null)
                        {
                            columns.Append(endpoint.RawNewLineSeparator);
                        }
                        row.Append(columns);
                    }

                    var bufferRows = endpoint.BufferRows ?? Options.BufferRows;
                    while (await reader.ReadAsync())
                    {
                        rowCount++;
                        if (!first)
                        {
                            if (binary is false)
                            {
                                // if raw
                                if (endpoint.Raw is false)
                                {
                                    row.Append(Consts.Comma);
                                }
                                else if (endpoint.RawNewLineSeparator is not null)
                                {
                                    row.Append(endpoint.RawNewLineSeparator);
                                }
                            }
                        }
                        else
                        {
                            first = false;
                        }

                        for (var i = 0; i < routineReturnRecordCount; i++)
                        {
                            if (binary is true)
                            {
                                await writer.WriteAsync(reader.GetFieldValue<byte[]>(0));
                            }
                            else
                            {
                                object value = reader.GetValue(i);
                                // AllResultTypesAreUnknown = true always returns string, except for null
                                var raw = (value == DBNull.Value ? "" : (string)value).AsSpan();

                                // if raw
                                if (endpoint.Raw)
                                {
                                    if (endpoint.RawValueSeparator is not null)
                                    {
                                        var descriptor = routine.ColumnsTypeDescriptor[i];
                                        if (descriptor.IsText || descriptor.IsDate || descriptor.IsDateTime)
                                        {
                                            row.Append(QuoteText(raw));
                                        }
                                        else
                                        {
                                            row.Append(raw);
                                        }
                                        if (i < routineReturnRecordCount - 1)
                                        {
                                            row.Append(endpoint.RawValueSeparator);
                                        }
                                    }
                                    else
                                    {
                                        row.Append(raw);
                                    }
                                }
                                else
                                {
                                    // Handle nested JSON composite types
                                    (string CompositeColumnName, string FieldName, bool IsFirstField, bool IsLastField, int FieldCount) compositeMapping = default;
                                    bool isInComposite = nestedJsonColumnMap is not null && nestedJsonColumnMap.TryGetValue(i, out compositeMapping);

                                    // Determine which buffer to write to
                                    StringBuilder outputBuffer = (isInComposite && compositeBuffer is not null) ? compositeBuffer : row;

                                    if (routine.ReturnsUnnamedSet == false)
                                    {
                                        if (i == 0)
                                        {
                                            row.Append(Consts.OpenBrace);
                                        }

                                        if (isInComposite)
                                        {
                                            if (compositeMapping.IsFirstField)
                                            {
                                                // Start of composite: reset buffer and track column name
                                                compositeBuffer!.Clear();
                                                compositeHasNonNullValue = false;
                                                currentCompositeName = compositeMapping.CompositeColumnName;

                                                // Write field name/value to buffer
                                                outputBuffer.Append(Consts.DoubleQuote);
                                                outputBuffer.Append(compositeMapping.FieldName);
                                                outputBuffer.Append(Consts.DoubleQuoteColon);
                                            }
                                            else
                                            {
                                                // Middle or end field in composite: just output field name
                                                outputBuffer.Append(Consts.DoubleQuote);
                                                outputBuffer.Append(compositeMapping.FieldName);
                                                outputBuffer.Append(Consts.DoubleQuoteColon);
                                            }
                                        }
                                        else
                                        {
                                            row.Append(Consts.DoubleQuote);
                                            row.Append(routine.ColumnNames[i]);
                                            row.Append(Consts.DoubleQuoteColon);
                                        }
                                    }

                                    var descriptor = routine.ColumnsTypeDescriptor[i];
                                    if (value == DBNull.Value)
                                    {
                                        outputBuffer.Append(Consts.Null);
                                    }
                                    else if (descriptor.IsArray && value is not null)
                                    {
                                        if (isInComposite) compositeHasNonNullValue = true;
                                        // Check if this is an array of composite types - always serialize as nested JSON objects
                                        if (routine.ArrayCompositeColumnInfo is not null &&
                                            routine.ArrayCompositeColumnInfo.TryGetValue(i, out var arrayCompositeInfo))
                                        {
                                            outputBuffer.Append(PgCompositeArrayToJsonArray(raw, arrayCompositeInfo.FieldNames, arrayCompositeInfo.FieldDescriptors));
                                        }
                                        else
                                        {
                                            outputBuffer.Append(PgArrayToJsonArray(raw, descriptor));
                                        }
                                    }
                                    else if ((descriptor.IsNumeric || descriptor.IsBoolean || descriptor.IsJson) && value is not null)
                                    {
                                        if (isInComposite) compositeHasNonNullValue = true;
                                        if (descriptor.IsBoolean)
                                        {
                                            if (raw.Length == 1 && raw[0] == 't')
                                            {
                                                outputBuffer.Append(Consts.True);
                                            }
                                            else if (raw.Length == 1 && raw[0] == 'f')
                                            {
                                                outputBuffer.Append(Consts.False);
                                            }
                                            else
                                            {
                                                outputBuffer.Append(raw);
                                            }
                                        }
                                        else
                                        {
                                            // numeric and json
                                            outputBuffer.Append(raw);
                                        }
                                    }
                                    else
                                    {
                                        if (isInComposite) compositeHasNonNullValue = true;
                                        if (descriptor.ActualDbType == NpgsqlDbType.Unknown)
                                        {
                                            outputBuffer.Append(PgUnknownToJsonArray(ref raw));
                                        }
                                        else if (descriptor.NeedsEscape)
                                        {
                                            outputBuffer.Append(SerializeString(ref raw));
                                        }
                                        else
                                        {
                                            if (descriptor.IsDateTime)
                                            {
                                                outputBuffer.Append(QuoteDateTime(ref raw));
                                            }
                                            else
                                            {
                                                outputBuffer.Append(Quote(ref raw));
                                            }
                                        }
                                    }

                                    // Handle closing braces and commas for nested JSON composite types
                                    if (isInComposite && compositeMapping.IsLastField)
                                    {
                                        // End of composite: decide whether to output null or the buffered object
                                        row.Append(Consts.DoubleQuote);
                                        row.Append(currentCompositeName);
                                        row.Append(Consts.DoubleQuoteColon);

                                        if (compositeHasNonNullValue)
                                        {
                                            // At least one field has a value, output as object
                                            row.Append(Consts.OpenBrace);
                                            row.Append(compositeBuffer);
                                            row.Append(Consts.CloseBrace);
                                        }
                                        else
                                        {
                                            // All fields are NULL, output null
                                            row.Append(Consts.Null);
                                        }
                                    }
                                    else if (isInComposite)
                                    {
                                        // Add comma between composite fields
                                        outputBuffer.Append(Consts.Comma);
                                    }

                                    if (routine.ReturnsUnnamedSet == false && i == routine.ColumnCount - 1)
                                    {
                                        row.Append(Consts.CloseBrace);
                                    }

                                    // Add comma between columns (but not within composite fields which are handled above)
                                    if (!isInComposite && i < routine.ColumnCount - 1)
                                    {
                                        row.Append(Consts.Comma);
                                    }
                                    else if (isInComposite && compositeMapping.IsLastField && i < routine.ColumnCount - 1)
                                    {
                                        row.Append(Consts.Comma);
                                    }
                                }
                            }
                        } // end for

                        // Check if we've exceeded the cacheable row limit
                        if (shouldCache && maxCacheableRows.HasValue && rowCount > (ulong)maxCacheableRows.Value)
                        {
                            shouldCache = false;
                            cacheBuffer = null; // Release memory
                        }

                        if (bufferRows != 1 && rowCount % bufferRows == 0)
                        {
                            // Append to cache buffer before clearing row
                            if (shouldCache)
                            {
                                cacheBuffer!.Append(row);
                            }
                            WriteStringBuilderToWriter(row, writer);
                            await writer.FlushAsync();
                            row.Clear();
                        }
                    } // end while

                    if (binary is true)
                    {
                        await writer.FlushAsync();
                    }
                    else
                    {
                        if (row.Length > 0)
                        {
                            // Append remaining rows to cache buffer
                            if (shouldCache)
                            {
                                cacheBuffer!.Append(row);
                            }
                            WriteStringBuilderToWriter(row, writer);
                            await writer.FlushAsync();
                        }
                        if (routine.ReturnsSet && endpoint.Raw is false)
                        {
                            writer.Advance(Encoding.UTF8.GetBytes(Consts.CloseBracket.ToString().AsSpan(), writer.GetSpan(Encoding.UTF8.GetMaxByteCount(1))));
                            if (shouldCache)
                            {
                                cacheBuffer!.Append(Consts.CloseBracket);
                            }
                        }

                        // Store in cache if within limits
                        if (shouldCache && cacheBuffer is not null)
                        {
                            Options.CacheOptions.DefaultRoutineCache?.AddOrUpdate(endpoint, cacheKeys?.ToString()!, cacheBuffer.ToString());
                        }
                    }
                    return;
                } // end if (routine.ReturnsRecord == true)
            } // end if (routine.IsVoid is false)
        }
        catch (Exception exception)
        {
            if (exception is NpgsqlToHttpException npgsqlToHttpException)
            {
                if (context.Response.HasStarted is false)
                {
                    await Results.Problem(
                        type: npgsqlToHttpException.Mapping.Type,
                        statusCode: npgsqlToHttpException.Mapping.StatusCode,
                        title: npgsqlToHttpException.Mapping.Title ?? exception.Message,
                        detail: npgsqlToHttpException.SqlState).ExecuteAsync(context);
                }
                else
                {
                    context.Response.StatusCode = npgsqlToHttpException.Mapping.StatusCode;
                }
            }
            else if (exception is NpgsqlException npgsqlEx)
            {
                if (context.Response.HasStarted is false)
                {
                    await Results.Problem(
                        type: null,
                        statusCode: 500,
                        title: npgsqlEx.Message,
                        detail: npgsqlEx.SqlState).ExecuteAsync(context);
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }
            }
            else
            {
                if (context.Response.HasStarted is false)
                {
                    await Results.Problem(
                        type: null, // remove RFC URL
                        statusCode: 500,
                        title: "Internal Server Error",
                        detail: exception.Message).ExecuteAsync(context);
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }
            }

            if (endpoint.Upload is true)
            {
                uploadHandler?.OnError(connection, context, exception);
            }

            if (context.Response.StatusCode != 200 && context.Response.StatusCode != 205 && context.Response.StatusCode != 400)
            {
                Logger?.LogError(exception, "Error executing command: {commandText} mapped to endpoint: {Url}", commandText, endpoint.Path);
            }
        }
        finally
        {
            await writer.CompleteAsync();
            await context.Response.CompleteAsync();
            if (transaction is not null)
            {
                if (connection is not null && connection.State == ConnectionState.Open)
                {
                    if (shouldCommit)
                    {
                        await transaction.CommitAsync();
                    }
                }
            }
            if (connection is not null && shouldDispose is true)
            {
                await connection.DisposeAsync();
            }
        }
    }
    
    private async ValueTask<bool> PrepareCommand(
        NpgsqlConnection connection,
        NpgsqlCommand command,
        string commandText,
        HttpContext context,
        RoutineEndpoint endpoint,
        bool unknownResults)
    {
        await OpenConnectionAsync(connection, context, endpoint);
        command.CommandText = commandText;
        
        if (endpoint.CommandTimeout.HasValue)
        {
            command.CommandTimeout = endpoint.CommandTimeout.Value.Seconds;
        }

        if (Options.CommandCallbackAsync is not null)
        {
            await Options.CommandCallbackAsync(endpoint, command, context);
            if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
            {
                return false;
            }
        }
        if (unknownResults)
        {
            if (endpoint.Routine.UnknownResultTypeList is not null)
            {
                command.UnknownResultTypeList = endpoint.Routine.UnknownResultTypeList;
            }
            else
            {
                command.AllResultTypesAreUnknown = true;
            }
        }
        else
        {
            command.AllResultTypesAreUnknown = false;
        }
        return true;
    }
    
    private async ValueTask OpenConnectionAsync(NpgsqlConnection connection, HttpContext context, RoutineEndpoint endpoint)
    {
        if (connection.State != ConnectionState.Open)
        {
            if (Options.BeforeConnectionOpen is not null)
            {
                Options.BeforeConnectionOpen(connection, endpoint, context);
            }
            await connection.OpenRetryAsync(Options.ConnectionRetryOptions, context.RequestAborted);
        }
    }

    private static void SearializeHeader(NpgsqlRestOptions options, HttpContext context, ref string? headers)
    {
        if (Options.CustomRequestHeaders.Count > 0)
        {
            foreach (var header in Options.CustomRequestHeaders)
            {
                context.Request.Headers.Add(header);
            }
        }

        var sb = new StringBuilder();
        sb.Append('{');
        var i = 0;

        foreach (var header in context.Request.Headers)
        {
            if (i++ > 0)
            {
                sb.Append(',');
            }
            sb.Append(SerializeString(header.Key));
            sb.Append(':');
            sb.Append(SerializeString(header.Value.ToString()));
        }
        sb.Append('}');
        headers = sb.ToString();
    }

    private static void MapProxyResponseToParameters(
        Proxy.ProxyResponse proxyResponse,
        NpgsqlParameterCollection parameters,
        RoutineEndpoint endpoint)
    {
        var proxyOptions = Options.ProxyOptions;

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = (NpgsqlRestParameter)parameters[i];
            var paramName = parameter.ActualName ?? parameter.ParameterName;

            if (endpoint.ProxyResponseParameterNames?.Contains(paramName) != true)
            {
                continue;
            }

            if (string.Equals(paramName, proxyOptions.ResponseStatusCodeParameter, StringComparison.OrdinalIgnoreCase))
            {
                if (parameter.TypeDescriptor.IsText)
                {
                    parameter.Value = proxyResponse.StatusCode.ToString();
                }
                else
                {
                    parameter.Value = proxyResponse.StatusCode;
                }
            }
            else if (string.Equals(paramName, proxyOptions.ResponseBodyParameter, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = (object?)proxyResponse.Body ?? DBNull.Value;
            }
            else if (string.Equals(paramName, proxyOptions.ResponseHeadersParameter, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = (object?)proxyResponse.Headers ?? DBNull.Value;
            }
            else if (string.Equals(paramName, proxyOptions.ResponseContentTypeParameter, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = (object?)proxyResponse.ContentType ?? DBNull.Value;
            }
            else if (string.Equals(paramName, proxyOptions.ResponseSuccessParameter, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = proxyResponse.IsSuccess;
            }
            else if (string.Equals(paramName, proxyOptions.ResponseErrorMessageParameter, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = (object?)proxyResponse.ErrorMessage ?? DBNull.Value;
            }
        }
    }

    /// <summary>
    /// Check if a parameter is a proxy response parameter that will be filled in by the proxy response.
    /// </summary>
    private static bool IsProxyResponseParameter(RoutineEndpoint endpoint, NpgsqlRestParameter parameter)
    {
        if (!endpoint.IsProxy || !Options.ProxyOptions.Enabled || endpoint.ProxyResponseParameterNames is null)
        {
            return false;
        }

        var paramName = parameter.ActualName ?? parameter.ConvertedName;
        return paramName is not null && endpoint.ProxyResponseParameterNames.Contains(paramName);
    }

    private static void WriteStringBuilderToWriter(StringBuilder row, PipeWriter writer)
    {
        foreach (ReadOnlyMemory<char> chunk in row.GetChunks())
        {
            var buffer = writer.GetSpan(Encoding.UTF8.GetMaxByteCount(chunk.Length));
            int bytesWritten = Encoding.UTF8.GetBytes(chunk.Span, buffer);
            writer.Advance(bytesWritten);
        }
    }

    private async ValueTask ReturnErrorAsync(
        string message,
        bool log, HttpContext context,
        int statusCode = (int)HttpStatusCode.InternalServerError)
    {
        if (log)
        {
            Logger?.LogError("{message}", message);
        }
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = Text.Plain;
        await context.Response.WriteAsync(message);
        await context.Response.CompleteAsync();
    }

    private async ValueTask<bool> ValidateParametersAsync(
        NpgsqlParameterCollection parameters,
        RoutineEndpoint endpoint,
        HttpContext context)
    {
        if (endpoint.ParameterValidations is null)
        {
            return true;
        }

        foreach (var kvp in endpoint.ParameterValidations)
        {
            var originalParamName = kvp.Key;
            var rules = kvp.Value;

            // Find the parameter by original name
            NpgsqlRestParameter? parameter = null;
            for (var i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i] as NpgsqlRestParameter;
                if (p is not null && string.Equals(p.ActualName, originalParamName, StringComparison.Ordinal))
                {
                    parameter = p;
                    break;
                }
            }

            if (parameter is null)
            {
                continue;
            }

            // Get the value to validate - use OriginalStringValue for consistency
            var valueToValidate = parameter.OriginalStringValue;
            var convertedParamName = parameter.ConvertedName;

            // Find rule names for error messages
            foreach (var rule in rules)
            {
                // Find the rule name by looking it up in ValidationOptions
                var ruleName = FindRuleName(rule);

                var (isValid, message) = ValidateParameterValue(
                    valueToValidate,
                    parameter.Value,
                    rule,
                    originalParamName,
                    convertedParamName,
                    ruleName);

                if (!isValid)
                {
                    var urlInfo = string.Concat(endpoint.Method.ToString(), " ", endpoint.Path);
                    Logger?.ValidationFailed(urlInfo, originalParamName, message);

                    context.Response.StatusCode = rule.StatusCode;
                    context.Response.ContentType = Text.Plain;
                    await context.Response.WriteAsync(message);
                    await context.Response.CompleteAsync();
                    return false;
                }
            }
        }

        return true;
    }

    private static string FindRuleName(ValidationRule rule)
    {
        foreach (var kvp in Options.ValidationOptions.Rules)
        {
            if (ReferenceEquals(kvp.Value, rule))
            {
                return kvp.Key;
            }
        }
        return string.Empty;
    }

    private static (bool IsValid, string Message) ValidateParameterValue(
        string? originalStringValue,
        object? value,
        ValidationRule rule,
        string originalParamName,
        string convertedParamName,
        string ruleName)
    {
        // Message format: {0} = original param name, {1} = converted param name, {2} = rule name
        var message = string.Format(rule.Message, originalParamName, convertedParamName, ruleName);

        switch (rule.Type)
        {
            case ValidationType.NotNull:
                // Check if value is null or DBNull
                if (value is null || value == DBNull.Value)
                {
                    return (false, message);
                }
                break;

            case ValidationType.NotEmpty:
                // Check if value is an empty string (null values pass - use NotNull for null check)
                if (originalStringValue is not null && originalStringValue.Length == 0)
                {
                    return (false, message);
                }
                break;

            case ValidationType.Required:
                // Combines NotNull and NotEmpty - value cannot be null or empty string
                if (value is null || value == DBNull.Value)
                {
                    return (false, message);
                }
                if (originalStringValue is not null && originalStringValue.Length == 0)
                {
                    return (false, message);
                }
                break;

            case ValidationType.Regex:
                if (rule.Pattern is null)
                {
                    return (true, string.Empty);
                }
                // Null/empty values don't match regex (use NotEmpty rule first if required)
                if (string.IsNullOrEmpty(originalStringValue))
                {
                    return (false, message);
                }
                if (!System.Text.RegularExpressions.Regex.IsMatch(originalStringValue, rule.Pattern))
                {
                    return (false, message);
                }
                break;

            case ValidationType.MinLength:
                if (rule.MinLength is null)
                {
                    return (true, string.Empty);
                }
                var strValue = originalStringValue ?? string.Empty;
                if (strValue.Length < rule.MinLength.Value)
                {
                    return (false, message);
                }
                break;

            case ValidationType.MaxLength:
                if (rule.MaxLength is null)
                {
                    return (true, string.Empty);
                }
                var strVal = originalStringValue ?? string.Empty;
                if (strVal.Length > rule.MaxLength.Value)
                {
                    return (false, message);
                }
                break;
        }

        return (true, string.Empty);
    }
}