using System;
using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using Npgsql;
using NpgsqlRest.Auth;
using NpgsqlRest.UploadHandlers;
using NpgsqlRest.UploadHandlers.Handlers;
using NpgsqlTypes;
using static System.Net.Mime.MediaTypeNames;
using static NpgsqlRest.ParameterParser;

namespace NpgsqlRest;

public class NpgsqlRestMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    private static ILogger? logger = default;
    private static IServiceProvider serviceProvider = default!;

    internal static NpgsqlRestMetadata metadata = default!;
    internal static Dictionary<string, NpgsqlRestMetadataEntry>.AlternateLookup<ReadOnlySpan<char>> lookup;
    internal static NpgsqlRestOptions Options { get; private set; } = default!;

    public static ILogger? Logger => logger;
    internal static void SetLogger(ILogger? logger) => NpgsqlRestMiddleware.logger = logger;

    internal static void SetOptions(NpgsqlRestOptions options)
    {
        Options = options;
    }

    internal static void SetMetadata(NpgsqlRestMetadata metadata)
    {
        NpgsqlRestMiddleware.metadata = metadata;
        lookup = metadata.Entries.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    internal static NpgsqlRestMetadata Metadata => metadata;
    internal static void SetServiceProvider(IServiceProvider serviceProvider) => NpgsqlRestMiddleware.serviceProvider = serviceProvider;

    public async Task InvokeAsync(HttpContext context)
    {
        Span<char> pathKeybuffer = stackalloc char[NpgsqlRestMetadataBuilder.MaxKeyLength];
        var position = 0;
        context.Request.Method.CopyTo(pathKeybuffer[position..]);
        position += context.Request.Method.Length;
        var path = context.Request.Path.ToString();
        path.CopyTo(pathKeybuffer[position..]);
        position += path.Length;
        var pathKey = pathKeybuffer[..position];

        if (!lookup.TryGetValue(pathKey, out var entry))
        {
            if (pathKey[^1] != '/')
            {
                pathKeybuffer[position] = '/';
                position += 1;
                var pathKey2 = pathKeybuffer[..position];
                if (!lookup.TryGetValue(pathKey2, out entry))
                {
                    await _next(context);
                    return;
                }
            }
            else
            {
                var pathKey2 = pathKeybuffer[..(position - 1)];
                if (!lookup.TryGetValue(pathKey2, out entry))
                {
                    await _next(context);
                    return;
                }
            }
        }

        if (entry is null)
        {
            await _next(context);
            return;
        }

        Routine routine = entry.Endpoint.Routine;
        RoutineEndpoint endpoint = entry.Endpoint;
        IRoutineSourceParameterFormatter formatter = entry.Formatter;

        string? headers = null;
        if (endpoint.RequestHeadersMode == RequestHeadersMode.Context)
        {
            SearializeHeader(Options, context, ref headers);
        }

        if (endpoint.Login is false)
        {
            if ((endpoint.RequiresAuthorization || endpoint.AuthorizeRoles is not null) && context.User?.Identity?.IsAuthenticated is false)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.CompleteAsync();
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
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    await context.Response.CompleteAsync();
                    return;
                }
            }
        }

        NpgsqlConnection? connection = null;
        NpgsqlTransaction? transaction = null;
        string? commandText = null;
        bool shouldDispose = true;
        bool shouldCommit = true;
        IUploadHandler? uploadHandler = null;

        var writer = System.IO.Pipelines.PipeWriter.Create(context.Response.Body);
        try
        {
            if (endpoint.ConnectionName is not null)
            {
                if (Options.ConnectionStrings?.TryGetValue(endpoint.ConnectionName, out var connectionString) is true)
                {
                    connection = new(connectionString);
                }
                else
                {
                    await ReturnErrorAsync($"Connection name {endpoint.ConnectionName} could not be found in options ConnectionStrings dictionary.", true, context);
                    return;
                }
            }
            else if (Options.ServiceProviderMode != ServiceProviderObject.None)
            {
                if (Options.ServiceProviderMode == ServiceProviderObject.NpgsqlDataSource)
                {
                    connection = serviceProvider.GetRequiredService<NpgsqlDataSource>().CreateConnection();
                    await connection.OpenRetryAsync(Options.ConnectionRetryOptions, logger, context.RequestAborted);
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

            if ( (Options.LogConnectionNoticeEvents && logger != null) || endpoint.InfoEventsStreamingPath is not null)
            {
                connection.Notice += (sender, args) =>
                {
                    if (Options.LogConnectionNoticeEvents && logger != null)
                    {
                        NpgsqlRestLogger.LogConnectionNotice(logger, args.Notice, Options.LogConnectionNoticeEventsMode);
                    }
                    if (args.Notice.IsInfo())
                    {
                        NpgsqlRestNoticeEventSource
                            .Broadcaster
                            .Broadcast(new NoticeEvent(args.Notice, endpoint, context.Request.Headers[Options.ExecutionIdHeaderName].FirstOrDefault()));
                    }
                };
            }
            
            if (Options.AuthenticationOptions.BasicAuth.Enabled is true || endpoint.BasicAuth?.Enabled is true)
            {
                if (Options.AuthenticationOptions.BasicAuth.ChallengeCommand is not null || endpoint.BasicAuth?.ChallengeCommand is not null)
                {
                    await OpenConnectionAsync(connection, context, endpoint);
                }
                await BasicAuthHandler.HandleAsync(context, endpoint, Options, connection, logger);
                if (context.Response.HasStarted is true)
                {
                    return;
                }
            }
            
            await using var command = NpgsqlRestCommand.Create(connection);

            var shouldLog = Options.LogCommands && logger != null;
            StringBuilder? cmdLog = shouldLog ?
                new(string.Concat("-- ", context.Request.Method, " ", context.Request.GetDisplayUrl(), Environment.NewLine)) :
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
            uploadHandler = endpoint.Upload is true ? Options.CreateUploadHandler(endpoint, logger) : null;
            int uploadMetaParamIndex = -1;
            Dictionary<string, object>? claimsDict = null;
            
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
                        logger?.CouldNotParseJson(body, context.Request.Path, e.Message);
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
                            logger?.CouldNotParseJson(body, context.Request.Path, e.Message);
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
                    await _next(context);
                    return;
                }

                if (queryDict.Count != routine.ParamCount && metadata.Overloads.Count > 0)
                {
                    if (metadata.Overloads.TryGetValue(string.Concat(entry.Key, queryDict.Count), out var overload))
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
                                    cacheKeys?.Append(parameter.GetCacheStringValue());
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
                                object value = parameter.NpgsqlValue!;
                                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                    "***" :
                                    FormatParam(value, parameter.TypeDescriptor);
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
                                        cacheKeys?.Append(parameter.GetCacheStringValue());
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
                                    object value = parameter.NpgsqlValue!;
                                    var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                        "***" :
                                        FormatParam(value, parameter.TypeDescriptor);
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
                                    cacheKeys?.Append(parameter.GetCacheStringValue());
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
                                object value = parameter.NpgsqlValue!;
                                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                    "***" :
                                    FormatParam(value, parameter.TypeDescriptor);
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
                                if (parameter.TypeDescriptor.HasDefault is false)
                                {
                                    shouldCommit = false;
                                    uploadHandler?.OnError(connection, context, null);
                                    await _next(context);
                                    return;
                                }
                                continue;
                            }
                        }
                    }

                    if (parameter.Value is null && TryParseParameter(parameter, ref qsValue, endpoint.QueryStringNullHandling) is false)
                    {
                        if (parameter.TypeDescriptor.HasDefault is false)
                        {
                            shouldCommit = false;
                            uploadHandler?.OnError(connection, context, null);
                            await _next(context);
                            return;
                        }
                        continue;
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
                            cacheKeys?.Append(parameter.GetCacheStringValue());
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
                        object value = parameter.NpgsqlValue!;
                        var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                            "***" :
                            FormatParam(value, parameter.TypeDescriptor);
                        cmdLog!.AppendLine(string.Concat(
                            "-- $",
                            paramIndex.ToString(),
                            " ", parameter.TypeDescriptor.OriginalType,
                            " = ",
                            p));
                    }
                }

                foreach (var queryKey in queryDict.Keys)
                {
                    if (routine.ParamsHash.Contains(queryKey) is false)
                    {
                        shouldCommit = false;
                        uploadHandler?.OnError(connection, context, null);
                        await _next(context);
                        return;
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
                    await _next(context);
                    return;
                }

                if (bodyDict.Count != routine.ParamCount && metadata.Overloads.Count > 0)
                {
                    if (metadata.Overloads.TryGetValue(string.Concat(entry.Key, bodyDict.Count), out var overload))
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
                                    cacheKeys?.Append(parameter.GetCacheStringValue());
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
                                object pvalue = parameter.NpgsqlValue!;
                                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                    "***" :
                                    FormatParam(pvalue, parameter.TypeDescriptor);
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
                                if (parameter.TypeDescriptor.HasDefault is false)
                                {
                                    transaction?.RollbackAsync();
                                    await _next(context);
                                    return;
                                }
                                continue;
                            }
                        }
                    }

                    if (parameter.Value is null && TryParseParameter(parameter, value) is false)
                    {
                        if (parameter.TypeDescriptor.HasDefault is false)
                        {
                            shouldCommit = false;
                            uploadHandler?.OnError(connection, context, null);
                            await _next(context);
                            return;
                        }
                        continue;
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
                            cacheKeys?.Append(parameter.GetCacheStringValue());
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
                        object pvalue = parameter.NpgsqlValue!;
                        var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                            "***" :
                            FormatParam(pvalue, parameter.TypeDescriptor);
                        cmdLog!.AppendLine(string.Concat(
                            "-- $",
                            paramIndex.ToString(),
                            " ", parameter.TypeDescriptor.OriginalType,
                            " = ",
                            p));
                    }
                }

                foreach (var bodyKey in bodyDict.Keys)
                {
                    if (routine.ParamsHash.Contains(bodyKey) is false)
                    {
                        shouldCommit = false;
                        uploadHandler?.OnError(connection, context, null);
                        await _next(context);
                        return;
                    }
                }
            } // end of json body parameters

            if (hasNulls && routine.IsStrict)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                await context.Response.CompleteAsync();
                return;
            }

            // paramsList is ready
            
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
                await _next(context);
                return;
            }

            Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>>? lookup = null;
            if (endpoint.HeadersNeedParsing is true || endpoint.CustomParamsNeedParsing)
            {
                Dictionary<string, string> replacements = [];
                for (var i = 0; i < command.Parameters.Count; i++)
                {
                    var value = command.Parameters[i].Value == DBNull.Value ? "" : command.Parameters[i].Value?.ToString() ?? "";
                    replacements[((NpgsqlRestParameter)command.Parameters[i]).ActualName] = value;
                    replacements[((NpgsqlRestParameter)command.Parameters[i]).ConvertedName] = value;
                }
                lookup = replacements.GetAlternateLookup<ReadOnlySpan<char>>();

                if (endpoint.CustomParamsNeedParsing && endpoint.CustomParameters is not null)
                {
                    foreach (var (key, value) in endpoint.CustomParameters)
                    {
                        if (value is not null)
                        {
                            endpoint.CustomParameters[key] = Formatter.FormatString(value, lookup!.Value).ToString();
                        }
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

            object? uploadMetadata = null;
            if (endpoint.Upload is true && uploadHandler is not null)
            {
                if (connection.State != ConnectionState.Open)
                {
                    if (Options.BeforeConnectionOpen is not null)
                    {
                        Options.BeforeConnectionOpen(connection, endpoint, context);
                    }
                    await connection.OpenRetryAsync(Options.ConnectionRetryOptions, logger, context.RequestAborted);
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

            if (
                (endpoint.RequestHeadersMode == RequestHeadersMode.Context && headers is not null && Options.RequestHeadersContextKey is not null) 
                ||
                (endpoint.UserContext is true && Options.AuthenticationOptions.IpAddressContextKey is not null)
                ||
                (endpoint.UserContext is true && context.User?.Identity?.IsAuthenticated is true &&
                    (Options.AuthenticationOptions.ClaimsJsonContextKey is not null || Options.AuthenticationOptions.ContextKeyClaimsMapping.Count > 0)
                    )
                ||
                (Options.UploadOptions.UseDefaultUploadMetadataContextKey && 
                    Options.UploadOptions.DefaultUploadMetadataContextKey is not null && 
                    endpoint.Upload is true && 
                    uploadHandler is not null && 
                    uploadMetadata is not null)
                )
            {
                if (connection.State != ConnectionState.Open)
                {
                    if (Options.BeforeConnectionOpen is not null)
                    {
                        Options.BeforeConnectionOpen(connection, endpoint, context);
                    }
                    await connection.OpenRetryAsync(Options.ConnectionRetryOptions, logger, context.RequestAborted);
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

                if (Options.UploadOptions.UseDefaultUploadMetadataContextKey &&
                    Options.UploadOptions.DefaultUploadMetadataContextKey is not null &&
                    endpoint.Upload is true &&
                    uploadHandler is not null &&
                    uploadMetadata is not null)
                {
                    var cmd = new NpgsqlBatchCommand(Consts.SetContext);
                    cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.UploadOptions.DefaultUploadMetadataContextKey));
                    cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(uploadMetadata));
                    batch.BatchCommands.Add(cmd);
                }
                await batch.ExecuteBatchWithRetryAsync(endpoint.RetryStrategy, logger, context.RequestAborted);
            }
            
            if (endpoint.Login is true)
            {
                if (await PrepareCommand(connection, command, commandText, context, endpoint, false) is false)
                {
                    return;
                }

                await LoginHandler.HandleAsync(
                    command,
                    context,
                    Options,
                    endpoint.RetryStrategy,
                    logger,
                    tracePath: path,
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

                await LogoutHandler.HandleAsync(command, endpoint, context, logger);
                return;
            }
            
            if (routine.IsVoid)
            {
                if (await PrepareCommand(connection, command, commandText, context, endpoint, true) is false)
                {
                    return;
                }

                await command.ExecuteNonQueryWithRetryAsync(endpoint.RetryStrategy, logger, context.RequestAborted);
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }
            else // end if (routine.IsVoid)
            {
                if (routine.ReturnsSet == false && routine.ColumnCount == 1 && routine.ReturnsRecordType is false)
                {
                    TypeDescriptor descriptor = routine.ColumnsTypeDescriptor[0];

                    object? valueResult;
                    if (endpoint.Cached is true)
                    {
                        if (Options.DefaultRoutineCache.Get(endpoint, cacheKeys?.ToString()!, out valueResult) is false)
                        {
                            if (await PrepareCommand(connection, command, commandText, context, endpoint, true) is false)
                            {
                                return;
                            }
                            
                            await using var reader = await command.ExecuteReaderWithRetryAsync(
                                CommandBehavior.SequentialAccess, 
                                endpoint.RetryStrategy, 
                                logger, 
                                context.RequestAborted);
                            if (shouldLog)
                            {
                                NpgsqlRestLogger.LogEndpoint(logger, endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                            }
                            if (await reader.ReadAsync())
                            {
                                valueResult = descriptor.IsBinary ? reader.GetFieldValue<byte[]>(0) : reader.GetValue(0) as string;
                                Options.DefaultRoutineCache.AddOrUpdate(endpoint, cacheKeys?.ToString()!, valueResult);
                            }
                            else
                            {
                                logger?.CouldNotReadCommand(command.CommandText, context.Request.Method, context.Request.Path);
                                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                                return;
                            }
                        }
                        else
                        {
                            if (shouldLog)
                            {
                                cmdLog?.AppendLine("/* from cache */");
                                NpgsqlRestLogger.LogEndpoint(logger, endpoint, cmdLog?.ToString() ?? "", commandText);
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
                            logger, 
                            context.RequestAborted);
                        if (shouldLog)
                        {
                            NpgsqlRestLogger.LogEndpoint(logger, endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                        }
                        if (await reader.ReadAsync())
                        {
                            valueResult = descriptor.IsBinary ? reader.GetFieldValue<byte[]>(0) : reader.GetValue(0) as string;
                        }
                        else
                        {
                            logger?.CouldNotReadCommand(command.CommandText, context.Request.Method, context.Request.Path);
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
                                PgConverters.PgArrayToJsonArray((valueResult as string).AsSpan(), descriptor) : (valueResult as string).AsSpan();
                            writer.Advance(Encoding.UTF8.GetBytes(span, writer.GetSpan(Encoding.UTF8.GetMaxByteCount(span.Length))));
                        }
                    }
                    return;

                }
                else // end if (routine.ReturnsRecord == false)
                {
                    if (await PrepareCommand(connection, command, commandText, context, endpoint, true) is false)
                    {
                        return;
                    }
                    var binary = routine.ColumnsTypeDescriptor.Length == 1 && routine.ColumnsTypeDescriptor[0].IsBinary;
                    await using var reader = await command.ExecuteReaderWithRetryAsync(
                        CommandBehavior.SequentialAccess, 
                        endpoint.RetryStrategy, 
                        logger, 
                        context.RequestAborted);
                    if (shouldLog)
                    {
                        NpgsqlRestLogger.LogEndpoint(logger, endpoint, cmdLog?.ToString() ?? "", command.CommandText);
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

                    if (routine.ReturnsSet && endpoint.Raw is false && binary is false)
                    {
                        writer.Advance(Encoding.UTF8.GetBytes(Consts.OpenBracket.ToString().AsSpan(), writer.GetSpan(Encoding.UTF8.GetMaxByteCount(1))));
                    }

                    bool first = true;
                    var routineReturnRecordCount = routine.ColumnCount;

                    StringBuilder row = new();
                    ulong rowCount = 0;

                    if (endpoint.Raw is true && endpoint.RawColumnNames is true && binary is false)
                    {
                        StringBuilder columns = new();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            if (endpoint.RawValueSeparator is not null && i > 0)
                            {
                                columns.Append(endpoint.RawValueSeparator);
                            }
                            columns.Append(PgConverters.QuoteText(reader.GetName(i).AsSpan()));
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
                                            row.Append(PgConverters.QuoteText(raw));
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
                                    if (routine.ReturnsUnnamedSet == false)
                                    {
                                        if (i == 0)
                                        {
                                            row.Append(Consts.OpenBrace);
                                        }
                                        row.Append(Consts.DoubleQuote);
                                        row.Append(routine.ColumnNames[i]);
                                        row.Append(Consts.DoubleQuoteColon);
                                    }

                                    var descriptor = routine.ColumnsTypeDescriptor[i];
                                    if (value == DBNull.Value)
                                    {
                                        row.Append(Consts.Null);
                                    }
                                    else if (descriptor.IsArray && value is not null)
                                    {
                                        row.Append(PgConverters.PgArrayToJsonArray(raw, descriptor));
                                    }
                                    else if ((descriptor.IsNumeric || descriptor.IsBoolean || descriptor.IsJson) && value is not null)
                                    {
                                        if (descriptor.IsBoolean)
                                        {
                                            if (raw.Length == 1 && raw[0] == 't')
                                            {
                                                row.Append(Consts.True);
                                            }
                                            else if (raw.Length == 1 && raw[0] == 'f')
                                            {
                                                row.Append(Consts.False);
                                            }
                                            else
                                            {
                                                row.Append(raw);
                                            }
                                        }
                                        else
                                        {
                                            // numeric and json
                                            row.Append(raw);
                                        }
                                    }
                                    else
                                    {
                                        if (descriptor.ActualDbType == NpgsqlDbType.Unknown)
                                        {
                                            row.Append(PgConverters.PgUnknownToJsonArray(ref raw));
                                        }
                                        else if (descriptor.NeedsEscape)
                                        {
                                            row.Append(PgConverters.SerializeString(ref raw));
                                        }
                                        else
                                        {
                                            if (descriptor.IsDateTime)
                                            {
                                                row.Append(PgConverters.QuoteDateTime(ref raw));
                                            }
                                            else
                                            {
                                                row.Append(PgConverters.Quote(ref raw));
                                            }
                                        }
                                    }
                                    if (routine.ReturnsUnnamedSet == false && i == routine.ColumnCount - 1)
                                    {
                                        row.Append(Consts.CloseBrace);
                                    }
                                    if (i < routine.ColumnCount - 1)
                                    {
                                        row.Append(Consts.Comma);
                                    }
                                }
                            }
                        } // end for

                        if (bufferRows != 1 && rowCount % bufferRows == 0)
                        {
                            foreach (ReadOnlyMemory<char> chunk in row.GetChunks())
                            {
                                var buffer = writer.GetSpan(Encoding.UTF8.GetMaxByteCount(chunk.Length));
                                int bytesWritten = Encoding.UTF8.GetBytes(chunk.Span, buffer);
                                writer.Advance(bytesWritten);
                            }
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
                            foreach (ReadOnlyMemory<char> chunk in row.GetChunks())
                            {
                                var buffer = writer.GetSpan(Encoding.UTF8.GetMaxByteCount(chunk.Length));
                                int bytesWritten = Encoding.UTF8.GetBytes(chunk.Span, buffer);
                                writer.Advance(bytesWritten);
                            }
                            await writer.FlushAsync();
                        }
                        if (routine.ReturnsSet && endpoint.Raw is false)
                        {
                            writer.Advance(Encoding.UTF8.GetBytes(Consts.CloseBracket.ToString().AsSpan(), writer.GetSpan(Encoding.UTF8.GetMaxByteCount(1))));
                        }
                    }
                    return;
                } // end if (routine.ReturnsRecord == true)
            } // end if (routine.IsVoid is false)
        }
        catch (Exception exception)
        {
            if (exception is NpgsqlException npgsqlEx)
            {
                string? sqlState = npgsqlEx.SqlState;

                if (Options.PostgreSqlErrorCodeToHttpStatusCodeMapping.TryGetValue(sqlState ?? "", out var code))
                {
                    context.Response.StatusCode = code;
                }
                if (Options.ReturnNpgsqlExceptionMessage && context.Response.HasStarted is false)
                {
                    if (context.Response.StatusCode == 200)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    }
                    if (context.Response.StatusCode != 205) // 205 forbids writing
                    {
                        ReadOnlySpan<char> msg;
                        if (context.Response.StatusCode == 400)
                        {
                            msg = exception.Message.Replace(string.Concat(sqlState, ": "), "").AsSpan();
                        }
                        else
                        {
                            msg = exception.Message.AsSpan();
                        }
                        writer.Advance(Encoding.UTF8.GetBytes(msg, writer.GetSpan(Encoding.UTF8.GetMaxByteCount(msg.Length))));
                    }
                }
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                var msg = string.Format("Error in while processing NpgsqlRest request. Please check error logs for more details. Here is what we know: {0}", exception.Message);
                writer.Advance(Encoding.UTF8.GetBytes(msg, writer.GetSpan(Encoding.UTF8.GetMaxByteCount(msg.Length))));
            }

            if (endpoint.Upload is true)
            {
                uploadHandler?.OnError(connection, context, exception);
            }

            if (context.Response.StatusCode != 200 && context.Response.StatusCode != 205 && context.Response.StatusCode != 400)
            {
                logger?.LogError(exception, "Error executing command: {commandText} mapped to endpoint: {Url}", commandText, endpoint.Url);
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

        await _next(context);
        return;
    }
    
    private static async Task<bool> PrepareCommand(
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
            command.CommandTimeout = endpoint.CommandTimeout.Value;
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
    
    private static async Task OpenConnectionAsync(NpgsqlConnection connection, HttpContext context, RoutineEndpoint endpoint)
    {
        if (connection.State != ConnectionState.Open)
        {
            if (Options.BeforeConnectionOpen is not null)
            {
                Options.BeforeConnectionOpen(connection, endpoint, context);
            }
            await connection.OpenRetryAsync(Options.ConnectionRetryOptions, logger, context.RequestAborted);
        }
    }

    private static void SearializeHeader(NpgsqlRestOptions options, HttpContext context, ref string? headers)
    {
        if (options.CustomRequestHeaders.Count > 0)
        {
            foreach (var header in options.CustomRequestHeaders)
            {
                context.Request.Headers.Add(header);
            }
        }

        headers = "{";
        var i = 0;

        foreach (var header in context.Request.Headers)
        {
            if (i++ > 0)
            {
                headers = string.Concat(headers, ",");
            }
            headers = string.Concat(
                headers,
                PgConverters.SerializeString(header.Key),
                ":",
                PgConverters.SerializeString(header.Value.ToString()));
        }
        headers = string.Concat(headers, "}");
    }

    private static async Task ReturnErrorAsync(
        string message, 
        bool log, HttpContext context, 
        int statusCode = (int)HttpStatusCode.InternalServerError)
    {
        if (log)
        {
            logger?.LogError("{message}", message);
        }
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = Text.Plain;
        await context.Response.WriteAsync(message);
        await context.Response.CompleteAsync();
    }
}