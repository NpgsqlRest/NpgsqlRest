using System.Buffers;
using System.Collections.Frozen;
using System.Data;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using Npgsql;
using NpgsqlRest.Auth;
using NpgsqlRest.HttpClientType;
using NpgsqlRest.UploadHandlers;
using NpgsqlRest.UploadHandlers.Handlers;
using static System.Net.Mime.MediaTypeNames;
using static NpgsqlRest.ParameterParser;

namespace NpgsqlRest;

public partial class NpgsqlRestEndpoint(
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
        var shouldLog = Options.LogCommands && Logger != null;

        // Pooled StringBuilders - declared at top level to enable pooling in finally block
        StringBuilder? cmdLog = null;
        StringBuilder? cacheKeys = null;
        StringBuilder? commandTextBuilder = null;
        StringBuilder? rowBuilder = null;
        StringBuilder? compositeFieldBuffer = null;
        // Multi-command per-iteration builders — set to null after each iteration's Return,
        // so outer finally won't double-return; if iteration throws, finally still cleans up.
        StringBuilder? mcRowBuilder = null;
        StringBuilder? mcCompositeBuffer = null;

        // proxy_out: capture function output into a buffer instead of writing to the real response
        MemoryStream? proxyOutBuffer = null;
        Stream? proxyOutOriginalBody = null;
        if (endpoint.IsProxyOut && Options.ProxyOptions.Enabled)
        {
            proxyOutBuffer = new MemoryStream();
            proxyOutOriginalBody = context.Response.Body;
            context.Response.Body = proxyOutBuffer;
        }

        var writer = proxyOutBuffer is not null
            ? PipeWriter.Create(context.Response.Body, new StreamPipeWriterOptions(leaveOpen: true))
            : PipeWriter.Create(context.Response.Body);
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
                    await ReturnErrorAsync($"Connection name {endpoint.ConnectionName} could not be found in options DataSources or ConnectionStrings dictionaries.", true, context, cancellationToken);
                    return;
                }
            }
            else if (Options.ServiceProviderMode != ServiceProviderObject.None)
            {
                if (serviceProvider is null)
                {
                    await ReturnErrorAsync($"ServiceProvider must be provided when ServiceProviderMode is set to {Options.ServiceProviderMode}.", true, context, cancellationToken);
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
                await ReturnErrorAsync("Connection did not initialize!", log: true, context, cancellationToken);
                return;
            }

            // Three reasons to attach a notice handler:
            //   1. LogConnectionNoticeEvents — log every notice regardless of SSE wiring.
            //   2. Endpoint publishes to broadcaster — forward matching notices to subscribers.
            //   3. WarnUnboundSseNotices — and the project actually uses SSE somewhere; warn when a
            //      RAISE in a non-publishing endpoint matches the SSE forwarding level (a likely
            //      missing @sse_publish annotation).
            bool noticeWarnEnabled = Options.WarnUnboundSseNotices
                && Logger != null
                && NpgsqlRestSseEventSource.HasAnySseEndpoints
                && endpoint.SsePublishEnabled is false;

            if ((Options.LogConnectionNoticeEvents && Logger != null)
                || endpoint.SsePublishEnabled
                || noticeWarnEnabled)
            {
                var currentEndpointCaptured = endpoint;
                connection.Notice += (sender, args) =>
                {
                    if (Options.LogConnectionNoticeEvents && Logger != null)
                    {
                        NpgsqlRestLogger.LogConnectionNotice(args.Notice, Options.LogConnectionNoticeEventsMode);
                    }
                    if (currentEndpointCaptured.SsePublishEnabled
                        && currentEndpointCaptured.SseEventNoticeLevel is not null
                        && string.Equals(args.Notice.Severity, currentEndpointCaptured.SseEventNoticeLevel.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        NpgsqlRestSseEventSource
                            .Broadcaster
                            .Broadcast(new SseEvent(args.Notice, currentEndpointCaptured, context.Request.Headers[Options.ExecutionIdHeaderName].FirstOrDefault()));
                    }
                    else if (noticeWarnEnabled
                        && string.Equals(args.Notice.Severity, Options.DefaultSseEventNoticeLevel.ToString(), StringComparison.OrdinalIgnoreCase)
                        && SseUnboundWarner.TryMarkWarned(currentEndpointCaptured.Path))
                    {
                        Logger!.UnboundSseRaise(args.Notice.Severity, currentEndpointCaptured.Path);
                    }
                };
            }
            
            if (Options.AuthenticationOptions.BasicAuth.Enabled is true || endpoint.BasicAuth?.Enabled is true)
            {
                if (Options.AuthenticationOptions.BasicAuth.ChallengeCommand is not null || endpoint.BasicAuth?.ChallengeCommand is not null)
                {
                    await OpenConnectionAsync(connection, context, endpoint, cancellationToken);
                }
                await BasicAuthHandler.HandleAsync(context, endpoint, connection, cancellationToken);
                if (context.Response.HasStarted is true)
                {
                    return;
                }
            }
            
            await using var command = NpgsqlRestCommand.Create(connection);

            if (shouldLog)
            {
                cmdLog = StringBuilderPool.Rent();
                cmdLog.Append("-- ");
                cmdLog.Append(context.Request.Method);
                cmdLog.Append(' ');
                if (Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth)
                {
                    cmdLog.Append(context.Request.Scheme);
                    cmdLog.Append("://");
                    cmdLog.Append(context.Request.Host);
                    cmdLog.Append(context.Request.Path);
                }
                else
                {
                    cmdLog.Append(context.Request.GetDisplayUrl());
                }
                cmdLog.Append(Environment.NewLine);
            }

            if (formatter.IsFormattable is false)
            {
                // Use pooled StringBuilder for non-formattable commands to avoid string.Concat allocations
                commandTextBuilder = StringBuilderPool.Rent(routine.Expression.Length + routine.ParamCount * 16);
                commandTextBuilder.Append(routine.Expression);
            }
            
            // paramsList
            bool hasNulls = false;
            int paramIndex = 0;
            JsonObject? jsonObj = null;
            Dictionary<string, JsonNode?>? bodyDict = null;
            string? body = null;
            // Use IQueryCollection directly to avoid ToDictionary() allocation
            IQueryCollection? queryCollection = null;
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
                var prefixLen = endpoint.CacheKeyPrefix?.Length ?? 0;
                cacheKeys = StringBuilderPool.Rent(routine.Expression.Length + prefixLen + (endpoint.CachedParams?.Count ?? 0) * 32);
                if (endpoint.CacheKeyPrefix is not null)
                {
                    cacheKeys.Append(endpoint.CacheKeyPrefix);
                    cacheKeys.Append(NpgsqlRestParameter.GetCacheKeySeparator());
                }
                cacheKeys.Append(routine.Expression);
            }

            if (endpoint.RequestParamType == RequestParamType.QueryString)
            {
                queryCollection = context.Request.Query;
            }
            if (endpoint.HasBodyParameter || endpoint.RequestParamType == RequestParamType.BodyJson)
            {
                context.Request.EnableBuffering();
                context.Request.Body.Position = 0;

                using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
                {
                    body = await reader.ReadToEndAsync(cancellationToken);
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
                if (queryCollection is null)
                {
                    shouldCommit = false;
                    uploadHandler?.OnError(connection, context, null);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.CompleteAsync();
                    return;
                }

                // Account for path parameters when counting query string parameters
                var pathParamCount = endpoint.PathParameters?.Length ?? 0;
                if (queryCollection.Count + pathParamCount != routine.ParamCount && overloads.Count > 0)
                {
                    if (overloads.TryGetValue(string.Concat(entry.Key, queryCollection.Count + pathParamCount), out var overload))
                    {
                        routine = overload.Endpoint.Routine;
                        endpoint = overload.Endpoint;
                        formatter = overload.Formatter;
                        if (formatter.IsFormattable is false)
                        {
                            // Reinitialize commandTextBuilder for the new routine
                            commandTextBuilder?.Clear();
                            if (commandTextBuilder is null)
                            {
                                commandTextBuilder = StringBuilderPool.Rent(routine.Expression.Length + routine.ParamCount * 16);
                            }
                            commandTextBuilder.Append(routine.Expression);
                        }
                    }
                }

                for (int i = 0; i < routine.Parameters.Length; i++)
                {
                    var parameter = routine.Parameters[i].NpgsqlRestParameterMemberwiseClone();

                    // Resolved parameter: skip HTTP filling, set to DBNull.Value placeholder.
                    // The actual value will be resolved via SQL expression after the parameter loop.
                    if (endpoint.ResolvedParameterExpressions is not null &&
                        (endpoint.ResolvedParameterExpressions.ContainsKey(parameter.ActualName) ||
                         endpoint.ResolvedParameterExpressions.ContainsKey(parameter.ConvertedName)))
                    {
                        parameter.Value = DBNull.Value;
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
                        if (!parameter.IsVirtual) command.Parameters.Add(parameter);
                        hasNulls = true;
                        if (formatter.IsFormattable is false)
                        {
                            if (formatter.RefContext)
                            {
                                commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex, context));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            else
                            {
                                commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex));
                            }
                        }
                        paramIndex++;
                        if (shouldLog && Options.LogCommandParameters)
                        {
                            cmdLog!.Append("-- $").Append(paramIndex).Append(' ')
                                .Append(parameter.TypeDescriptor.OriginalType)
                                .Append(" = (resolved)").AppendLine();
                        }
                        continue;
                    }

                    if (parameter.HashOf is not null)
                    {
                        var hashValueQueryCollection = queryCollection.TryGetValue(parameter.HashOf.ConvertedName, out var hashQsValue) ? hashQsValue.ToString() : null;
                        if (string.IsNullOrEmpty(hashValueQueryCollection) is true)
                        {
                            parameter.Value = DBNull.Value;
                        }
                        else
                        {
                            parameter.Value = Options.AuthenticationOptions.PasswordHasher?.HashPassword(hashValueQueryCollection) as object ?? DBNull.Value;
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
                            // Claim auto-bind always wins (security routines depend on it). If the
                            // request also supplied a value, it would otherwise be silently dropped —
                            // surface that as a WARN so a parameter naming collision is visible.
                            if (queryCollection is not null
                                && (queryCollection.ContainsKey(parameter.ConvertedName) || queryCollection.ContainsKey(parameter.ActualName)))
                            {
                                Logger?.ClaimMappedParamReceivedRequestValue(
                                    endpoint.Path, parameter.ActualName, "query", claimType);
                            }
                            parameter.Value = claimsDict!.GetClaimDbParam(claimType);
                        }
                        else if (context.User?.Identity?.IsAuthenticated is true && parameter.IsUserClaims is true)
                        {
                            parameter.Value = claimsDict?.GetUserClaimsDbParam() ?? DBNull.Value;
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
                            if (!parameter.IsVirtual) command.Parameters.Add(parameter);

                            if (hasNulls is false && parameter.Value == DBNull.Value)
                            {
                                hasNulls = true;
                            }

                            if (formatter.IsFormattable is false)
                            {
                                if (formatter.RefContext)
                                {
                                    commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex, context));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                else
                                {
                                    commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex));
                                }
                            }
                            paramIndex++;
                            if (shouldLog && Options.LogCommandParameters)
                            {
                                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                    "***" :
                                    FormatParameterForLog(parameter);
                                cmdLog!.Append("-- $").Append(paramIndex).Append(' ')
                                    .Append(parameter.TypeDescriptor.OriginalType)
                                    .Append(" = ").AppendLine(p);
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
                                if (!parameter.IsVirtual) command.Parameters.Add(parameter);
                                if (hasNulls is false && parameter.Value == DBNull.Value)
                                {
                                    hasNulls = true;
                                }

                                if (formatter.IsFormattable is false)
                                {
                                    if (formatter.RefContext)
                                    {
                                        commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex, context));
                                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                        {
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex));
                                    }
                                }
                                paramIndex++;
                                if (shouldLog && Options.LogCommandParameters)
                                {
                                    var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                        "***" :
                                        FormatParameterForLog(parameter);
                                    cmdLog!.Append("-- $").Append(paramIndex).Append(' ')
                                        .Append(parameter.TypeDescriptor.OriginalType)
                                        .Append(" = ").AppendLine(p);
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
                        if (queryCollection.ContainsKey(parameter.ConvertedName) is false)
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
                            if (!parameter.IsVirtual) command.Parameters.Add(parameter);

                            if (hasNulls is false && parameter.Value == DBNull.Value)
                            {
                                hasNulls = true;
                            }

                            if (formatter.IsFormattable is false)
                            {
                                if (formatter.RefContext)
                                {
                                    commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex, context));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                else
                                {
                                    commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex));
                                }
                            }
                            paramIndex++;
                            if (shouldLog && Options.LogCommandParameters)
                            {
                                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                    "***" :
                                    FormatParameterForLog(parameter);
                                cmdLog!.Append("-- $").Append(paramIndex).Append(' ')
                                    .Append(parameter.TypeDescriptor.OriginalType)
                                    .Append(" = ").AppendLine(p);
                            }

                            continue;
                        }
                    }

                    // path parameter - extract from RouteValues
                    if (parameter.Value is null && endpoint.HasPathParameters)
                    {
                        // Use optimized O(1) lookup via HashSet
                        string? matchedPathParam = endpoint.FindMatchingPathParameter(parameter.ConvertedName, parameter.ActualName);

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
                                if (!parameter.IsVirtual) command.Parameters.Add(parameter);

                                if (hasNulls is false && parameter.Value == DBNull.Value)
                                {
                                    hasNulls = true;
                                }

                                if (formatter.IsFormattable is false)
                                {
                                    if (formatter.RefContext)
                                    {
                                        commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex, context));
                                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                        {
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex));
                                    }
                                }
                                paramIndex++;
                                if (shouldLog && Options.LogCommandParameters)
                                {
                                    var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                        "***" :
                                        FormatParameterForLog(parameter);
                                    cmdLog!.Append("-- $").Append(paramIndex).Append(' ')
                                        .Append(parameter.TypeDescriptor.OriginalType)
                                        .Append(" = ").AppendLine(p);
                                }

                                continue;
                            }
                        }
                    }

                    if (queryCollection.TryGetValue(parameter.ConvertedName, out var qsValue) is false)
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
                                else if (parameter.DefaultValue is not null)
                                {
                                    // Explicit default value (SQL file annotation) — bind it
                                    parameter.Value = parameter.DefaultValue;
                                    parameter.ParamType = ParamType.QueryString;
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
                        else if (parameter.DefaultValue is not null)
                        {
                            parameter.Value = parameter.DefaultValue;
                            parameter.ParamType = ParamType.QueryString;
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
                            commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex, context));
                            if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return;
                            }
                        }
                        else
                        {
                            commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex));
                        }
                    }
                    paramIndex++;
                    if (shouldLog && Options.LogCommandParameters)
                    {
                        var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                            "***" :
                            FormatParameterForLog(parameter);
                        cmdLog!.Append("-- $").Append(paramIndex).Append(' ')
                            .Append(parameter.TypeDescriptor.OriginalType)
                            .Append(" = ").AppendLine(p);
                    }
                }

                // Skip query string validation for passthrough proxy endpoints - query will be forwarded as-is
                if (!(endpoint.IsProxy && Options.ProxyOptions.Enabled && !endpoint.HasProxyResponseParameters))
                {
                    foreach (var queryKey in queryCollection.Keys)
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
                        if (formatter.IsFormattable is false)
                        {
                            // Reinitialize commandTextBuilder for the new routine
                            commandTextBuilder?.Clear();
                            if (commandTextBuilder is null)
                            {
                                commandTextBuilder = StringBuilderPool.Rent(routine.Expression.Length + routine.ParamCount * 16);
                            }
                            commandTextBuilder.Append(routine.Expression);
                        }
                    }
                }

                for (int i = 0; i < routine.Parameters.Length; i++)
                {
                    var parameter = routine.Parameters[i].NpgsqlRestParameterMemberwiseClone();

                    // Resolved parameter: skip HTTP filling, set to DBNull.Value placeholder.
                    // The actual value will be resolved via SQL expression after the parameter loop.
                    if (endpoint.ResolvedParameterExpressions is not null &&
                        (endpoint.ResolvedParameterExpressions.ContainsKey(parameter.ActualName) ||
                         endpoint.ResolvedParameterExpressions.ContainsKey(parameter.ConvertedName)))
                    {
                        parameter.Value = DBNull.Value;
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
                        if (!parameter.IsVirtual) command.Parameters.Add(parameter);
                        hasNulls = true;
                        if (formatter.IsFormattable is false)
                        {
                            if (formatter.RefContext)
                            {
                                commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex, context));
                                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return;
                                }
                            }
                            else
                            {
                                commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex));
                            }
                        }
                        paramIndex++;
                        if (shouldLog && Options.LogCommandParameters)
                        {
                            cmdLog!.Append("-- $").Append(paramIndex).Append(' ')
                                .Append(parameter.TypeDescriptor.OriginalType)
                                .Append(" = (resolved)").AppendLine();
                        }
                        continue;
                    }

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
                            // Claim auto-bind always wins (security routines depend on it). If the
                            // request also supplied a value, it would otherwise be silently dropped —
                            // surface that as a WARN so a parameter naming collision is visible.
                            if (bodyDict is not null
                                && (bodyDict.ContainsKey(parameter.ConvertedName) || bodyDict.ContainsKey(parameter.ActualName)))
                            {
                                Logger?.ClaimMappedParamReceivedRequestValue(
                                    endpoint.Path, parameter.ActualName, "body", claimType);
                            }
                            parameter.Value = claimsDict!.GetClaimDbParam(claimType);
                        }
                        else if (context.User?.Identity?.IsAuthenticated is true && parameter.IsUserClaims is true)
                        {
                            parameter.Value = claimsDict?.GetUserClaimsDbParam() ?? DBNull.Value;
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
                            if (!parameter.IsVirtual) command.Parameters.Add(parameter);

                            if (hasNulls is false && parameter.Value == DBNull.Value)
                            {
                                hasNulls = true;
                            }

                            if (formatter.IsFormattable is false)
                            {
                                if (formatter.RefContext)
                                {
                                    commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex, context));
                                    if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                    {
                                        return;
                                    }
                                }
                                else
                                {
                                    commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex));
                                }
                            }
                            paramIndex++;
                            if (shouldLog && Options.LogCommandParameters)
                            {
                                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                    "***" :
                                    FormatParameterForLog(parameter);
                                cmdLog!.Append("-- $").Append(paramIndex).Append(' ')
                                    .Append(parameter.TypeDescriptor.OriginalType)
                                    .Append(" = ").AppendLine(p);
                            }

                            continue;
                        }
                    }

                    // path parameter - extract from RouteValues (for JSON body mode)
                    if (parameter.Value is null && endpoint.HasPathParameters)
                    {
                        // Use optimized O(1) lookup via HashSet
                        string? matchedPathParam = endpoint.FindMatchingPathParameter(parameter.ConvertedName, parameter.ActualName);

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
                                if (!parameter.IsVirtual) command.Parameters.Add(parameter);

                                if (hasNulls is false && parameter.Value == DBNull.Value)
                                {
                                    hasNulls = true;
                                }

                                if (formatter.IsFormattable is false)
                                {
                                    if (formatter.RefContext)
                                    {
                                        commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex, context));
                                        if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                                        {
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex));
                                    }
                                }
                                paramIndex++;
                                if (shouldLog && Options.LogCommandParameters)
                                {
                                    var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                                        "***" :
                                        FormatParameterForLog(parameter);
                                    cmdLog!.Append("-- $").Append(paramIndex).Append(' ')
                                        .Append(parameter.TypeDescriptor.OriginalType)
                                        .Append(" = ").AppendLine(p);
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
                                else if (parameter.DefaultValue is not null)
                                {
                                    // Explicit default value (SQL file annotation) — bind it
                                    parameter.Value = parameter.DefaultValue;
                                    parameter.ParamType = ParamType.BodyJson;
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
                        else if (parameter.DefaultValue is not null)
                        {
                            parameter.Value = parameter.DefaultValue;
                            parameter.ParamType = ParamType.BodyJson;
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
                            commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex, context));
                            if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return;
                            }
                        }
                        else
                        {
                            commandTextBuilder!.Append(formatter.AppendCommandParameter(parameter, paramIndex));
                        }
                    }
                    paramIndex++;
                    if (shouldLog && Options.LogCommandParameters)
                    {
                        var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && endpoint.IsAuth ?
                            "***" :
                            FormatParameterForLog(parameter);
                        cmdLog!.Append("-- $").Append(paramIndex).Append(' ')
                            .Append(parameter.TypeDescriptor.OriginalType)
                            .Append(" = ").AppendLine(p);
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
                if (!await ValidateParametersAsync(command.Parameters, endpoint, context, cancellationToken))
                {
                    return;
                }
            }

            // Encrypt parameters marked with encrypt annotation
            if (Options.AuthenticationOptions.DefaultDataProtector is not null &&
                (endpoint.EncryptAllParameters || endpoint.EncryptParameters is not null))
            {
                var protector = Options.AuthenticationOptions.DefaultDataProtector;
                for (int p = 0; p < command.Parameters.Count; p++)
                {
                    var param = (NpgsqlRestParameter)command.Parameters[p];
                    if (param.Value is string strValue &&
                        (endpoint.EncryptAllParameters ||
                         endpoint.EncryptParameters!.Contains(param.ActualName) ||
                         endpoint.EncryptParameters!.Contains(param.ConvertedName)))
                    {
                        param.Value = protector.Protect(strValue);
                    }
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
                        if (
                            string.Equals(claim.Type, Options.AuthenticationOptions.DefaultUserIdClaimType, StringComparison.Ordinal) ||
                            string.Equals(claim.Type, Options.AuthenticationOptions.DefaultNameClaimType, StringComparison.Ordinal) ||
                            string.Equals(claim.Type, Options.AuthenticationOptions.DefaultRoleClaimType, StringComparison.Ordinal))
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
                    commandTextBuilder!.Append(formatter.AppendEmpty(context));
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
                    commandTextBuilder!.Append(formatter.AppendEmpty());
                }
                // Convert StringBuilder to string for final commandText
                commandText = commandTextBuilder.ToString();
            }

            if (commandText is null)
            {
                shouldCommit = false;
                uploadHandler?.OnError(connection, context, null);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.CompleteAsync();
                return;
            }

            // Resolve parameter expressions: execute SQL expressions to fill resolved parameter values
            if (endpoint.ResolvedParameterExpressions is not null && endpoint.ResolvedParameterExpressions.Count > 0)
            {
                await OpenConnectionAsync(connection, context, endpoint, cancellationToken);
                foreach (var (resolvedParamName, sqlExpression) in endpoint.ResolvedParameterExpressions)
                {
                    var (parameterizedSql, sqlParams) = Formatter.ParameterizeSqlExpression(sqlExpression, command.Parameters);
                    await using var resolveCmd = new NpgsqlCommand(parameterizedSql, connection);
                    for (int p = 0; p < sqlParams.Count; p++)
                    {
                        resolveCmd.Parameters.Add(new NpgsqlParameter { Value = sqlParams[p].Value ?? DBNull.Value });
                    }
                    var resolvedValue = await resolveCmd.ExecuteScalarAsync(cancellationToken);

                    // Update the parameter value in the main command
                    for (int p = 0; p < command.Parameters.Count; p++)
                    {
                        var param = (NpgsqlRestParameter)command.Parameters[p];
                        if (string.Equals(param.ActualName, resolvedParamName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(param.ConvertedName, resolvedParamName, StringComparison.OrdinalIgnoreCase))
                        {
                            param.Value = resolvedValue ?? DBNull.Value;
                            break;
                        }
                    }
                }
            }

            Dictionary<string, string>? customParameters = endpoint.CustomParameters;
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
                    customParameters = new Dictionary<string, string>(endpoint.CustomParameters.Count);
                    foreach (var (key, value) in endpoint.CustomParameters)
                    {
                        customParameters[key] = Formatter.FormatString(value, lookup!.Value).ToString();
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
                // Set self base URL for relative path resolution (lazy, once)
                if (HttpClientTypeHandler.SelfBaseUrl is null)
                {
                    if (Options.HttpClientOptions.SelfBaseUrl is not null)
                    {
                        HttpClientTypeHandler.SelfBaseUrl = Options.HttpClientOptions.SelfBaseUrl.TrimEnd('/');
                    }
                    else
                    {
                        var server = context.RequestServices.GetService(typeof(Microsoft.AspNetCore.Hosting.Server.IServer)) as Microsoft.AspNetCore.Hosting.Server.IServer;
                        var addresses = server?.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
                        var addr = addresses?.Addresses.FirstOrDefault();
                        HttpClientTypeHandler.SelfBaseUrl = addr ?? $"{context.Request.Scheme}://{context.Request.Host}";
                    }
                }
                await HttpClientTypeHandler.InvokeAllAsync(customHttpTypes, lookup, command.Parameters, cancellationToken);
            }

            // Cache the cache key string once to avoid repeated ToString() allocations
            string? cacheKeyString = cacheKeys?.ToString();

            // Resolve cache backend: profile's instance if set, otherwise the root DefaultRoutineCache.
            var resolvedCache = endpoint.ResolvedCache ?? Options.CacheOptions.DefaultRoutineCache;

            // Evaluate the When rules once. First match decides:
            // - Skip = true  → bypass cache entirely (no read, no write).
            // - Skip = false → use rule's TTL override when writing (read still hits cache normally).
            // - No match     → no override; endpoint.CacheExpiresIn is used.
            // Invalidation requests are NOT subject to bypass — they must always remove on demand.
            var whenResult = endpoint.InvalidateCache is false ? EvaluateCacheWhenRules(endpoint, command) : default;
            bool bypassCache = whenResult.Skip;
            TimeSpan? cacheTtlOverride = whenResult.Skip ? null : whenResult.TtlOverride;

            // Handle reverse proxy endpoints
            if (endpoint.IsProxy && Options.ProxyOptions.Enabled)
            {
                // Set self base URL for relative path proxy resolution (lazy, once)
                if (Proxy.ProxyRequestHandler.SelfBaseUrl is null)
                {
                    if (Options.ProxyOptions.SelfBaseUrl is not null)
                    {
                        Proxy.ProxyRequestHandler.SelfBaseUrl = Options.ProxyOptions.SelfBaseUrl.TrimEnd('/');
                    }
                    else
                    {
                        var proxyServer = context.RequestServices.GetService(typeof(Microsoft.AspNetCore.Hosting.Server.IServer)) as Microsoft.AspNetCore.Hosting.Server.IServer;
                        var proxyAddresses = proxyServer?.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
                        var proxyAddr = proxyAddresses?.Addresses.FirstOrDefault();
                        Proxy.ProxyRequestHandler.SelfBaseUrl = proxyAddr ?? $"{context.Request.Scheme}://{context.Request.Host}";
                    }
                }

                // Check cache for passthrough proxy endpoints (those without proxy response parameters)
                bool isPassthroughProxy = !endpoint.HasProxyResponseParameters;
                if (isPassthroughProxy &&
                    endpoint.Cached is true &&
                    bypassCache is false &&
                    resolvedCache is not null)
                {
                    if (resolvedCache.Get(endpoint, cacheKeyString!, out var cachedProxyResponse))
                    {
                        // Cache hit - return cached proxy response
                        if (cachedProxyResponse is Proxy.ProxyResponse cached)
                        {
                            if (shouldLog)
                            {
                                cmdLog?.AppendLine("/* proxy response from cache */");
                                NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", commandText);
                            }
                            await Proxy.ProxyRequestHandler.WriteResponseAsync(context, cached, Options.ProxyOptions, cancellationToken);
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
                            var claimsJson = claimsDict.GetUserClaimsDbParam();
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
                    if (endpoint.Cached is true && bypassCache is false && resolvedCache is not null)
                    {
                        resolvedCache.AddOrUpdate(endpoint, cacheKeyString!, proxyResponse, cacheTtlOverride);
                    }
                    await Proxy.ProxyRequestHandler.WriteResponseAsync(context, proxyResponse, Options.ProxyOptions, cancellationToken);
                    return;
                }
            }

            // Set user context BEFORE upload so that upload row commands can access user claims.
            // Also runs BeforeRoutineCommands and (when WrapInTransaction is true) opens the request transaction here.
            bool willSetHeadersContext = endpoint.RequestHeadersMode == RequestHeadersMode.Context && headers is not null && Options.RequestHeadersContextKey is not null;
            bool willSetUserContext = endpoint.UserContext is true && (
                Options.AuthenticationOptions.IpAddressContextKey is not null
                || (context.User?.Identity?.IsAuthenticated is true &&
                    (Options.AuthenticationOptions.ClaimsJsonContextKey is not null || Options.AuthenticationOptions.ContextKeyClaimsMapping.Count > 0))
            );
            bool willRunBeforeRoutineCommands = Options.BeforeRoutineCommands.Length > 0;
            if (willSetHeadersContext || willSetUserContext || willRunBeforeRoutineCommands || Options.WrapInTransaction)
            {
                if (connection.State != ConnectionState.Open)
                {
                    if (Options.BeforeConnectionOpen is not null)
                    {
                        Options.BeforeConnectionOpen(connection, endpoint, context);
                    }
                    await connection.OpenRetryAsync(Options.ConnectionRetryOptions, cancellationToken);
                }

                if (Options.WrapInTransaction && transaction is null)
                {
                    transaction = await connection.BeginTransactionAsync(cancellationToken);
                }

                string setContextSql = transaction is not null ? Consts.SetContextLocal : Consts.SetContext;

                if (willSetHeadersContext || willSetUserContext || willRunBeforeRoutineCommands)
                {
                    await using var batch = NpgsqlRestBatch.Create(connection);

                    if (willSetHeadersContext)
                    {
                        var cmd = new NpgsqlBatchCommand(setContextSql);
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.RequestHeadersContextKey));
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(headers));
                        batch.BatchCommands.Add(cmd);
                    }

                    if (endpoint.UserContext is true)
                    {
                        claimsDict ??= context.User.BuildClaimsDictionary(Options.AuthenticationOptions);

                        if (Options.AuthenticationOptions.IpAddressContextKey is not null)
                        {
                            var cmd = new NpgsqlBatchCommand(setContextSql);
                            cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.AuthenticationOptions.IpAddressContextKey));
                            cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(context.Request.GetClientIpAddressDbParam()));
                            batch.BatchCommands.Add(cmd);
                        }
                        if (context.User?.Identity?.IsAuthenticated is true && Options.AuthenticationOptions.ClaimsJsonContextKey is not null)
                        {
                            var cmd = new NpgsqlBatchCommand(setContextSql);
                            cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.AuthenticationOptions.ClaimsJsonContextKey));
                            cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(claimsDict?.GetUserClaimsDbParam() ?? DBNull.Value));
                            batch.BatchCommands.Add(cmd);
                        }
                        if (context.User?.Identity?.IsAuthenticated is true)
                        {
                            foreach (var mapping in Options.AuthenticationOptions.ContextKeyClaimsMapping)
                            {
                                var cmd = new NpgsqlBatchCommand(setContextSql);
                                cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(mapping.Key));
                                cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(claimsDict!.GetClaimDbContextParam(mapping.Value)));
                                batch.BatchCommands.Add(cmd);
                            }
                        }
                    }

                    if (willRunBeforeRoutineCommands)
                    {
                        foreach (var beforeCmd in Options.BeforeRoutineCommands)
                        {
                            var cmd = new NpgsqlBatchCommand(beforeCmd.Sql);
                            foreach (var p in beforeCmd.Parameters)
                            {
                                object value;
                                switch (p.Source)
                                {
                                    case BeforeRoutineCommandParameterSource.Claim:
                                        claimsDict ??= context.User.BuildClaimsDictionary(Options.AuthenticationOptions);
                                        value = p.Name is null ? DBNull.Value : claimsDict!.GetClaimDbContextParam(p.Name);
                                        break;
                                    case BeforeRoutineCommandParameterSource.RequestHeader:
                                        value = p.Name is not null && context.Request.Headers.TryGetValue(p.Name, out var headerValue)
                                            ? (object)headerValue.ToString()
                                            : DBNull.Value;
                                        break;
                                    case BeforeRoutineCommandParameterSource.IpAddress:
                                        value = context.Request.GetClientIpAddressDbParam();
                                        break;
                                    default:
                                        value = DBNull.Value;
                                        break;
                                }
                                cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(value));
                            }
                            batch.BatchCommands.Add(cmd);
                        }
                    }

                    await batch.ExecuteBatchWithRetryAsync(endpoint.RetryStrategy, cancellationToken);
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
                    await connection.OpenRetryAsync(Options.ConnectionRetryOptions, cancellationToken);
                }
                if (uploadHandler.RequiresTransaction is true && transaction is null)
                {
                    transaction = await connection.BeginTransactionAsync(cancellationToken);
                }
                uploadMetadata = await uploadHandler.UploadAsync(connection, context, customParameters, cancellationToken);
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
                var cmd = new NpgsqlBatchCommand(transaction is not null ? Consts.SetContextLocal : Consts.SetContext);
                cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.UploadOptions.DefaultUploadMetadataContextKey));
                cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(uploadMetadata));
                batch.BatchCommands.Add(cmd);
                await batch.ExecuteBatchWithRetryAsync(endpoint.RetryStrategy, cancellationToken);
            }
            
            if (endpoint.Login is true)
            {
                if (await PrepareCommand(connection, command, commandText, context, endpoint, false, cancellationToken) is false)
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
                    cancellationToken,
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
                if (await PrepareCommand(connection, command, commandText, context, endpoint, true, cancellationToken) is false)
                {
                    return;
                }
                if (shouldLog)
                {
                    NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                }
                await LogoutHandler.HandleAsync(command, endpoint, context, cancellationToken);
                return;
            }

            // Handle cache invalidation endpoint. Routes through the endpoint's resolved cache (profile or root),
            // never bypassed by When rules — invalidation must always work on demand.
            if (endpoint.InvalidateCache is true && resolvedCache is not null && cacheKeys is not null)
            {
                var cacheKey = cacheKeys.ToString();
                var removed = resolvedCache.Remove(cacheKey);
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

            // Multi-command rendering: execute each statement separately, build JSON object
            if (routine.IsMultiCommand && routine.MultiCommandInfo is not null)
            {
                await OpenConnectionAsync(connection, context, endpoint, cancellationToken);

                if (shouldLog)
                {
                    if (routine.LogCommandText)
                    {
                        var allSql = string.Join(";\n", routine.MultiCommandInfo.Select(c => c.Statement));
                        NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", allSql);
                    }
                    else
                    {
                        NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "",
                            string.Concat(routine.SimpleDefinition, " (", routine.MultiCommandInfo.Length.ToString(), " statements)"));
                    }
                }

                // Void multi-command: execute all statements, return 204
                if (endpoint.Void)
                {
                    for (int cmdIndex = 0; cmdIndex < routine.MultiCommandInfo.Length; cmdIndex++)
                    {
                        var currentCmd = routine.MultiCommandInfo[cmdIndex];
                        await using var mcCmd = new NpgsqlCommand(currentCmd.Statement, connection);
                        if (endpoint.CommandTimeout.HasValue)
                        {
                            mcCmd.CommandTimeout = endpoint.CommandTimeout.Value.Seconds;
                        }
                        for (int pi = 0; pi < Math.Min(currentCmd.ParamCount, command.Parameters.Count); pi++)
                        {
                            var srcParam = command.Parameters[pi];
                            mcCmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = srcParam.NpgsqlDbType,
                                Value = srcParam.Value ?? DBNull.Value
                            });
                        }
                        await mcCmd.ExecuteNonQueryWithRetryAsync(
                            endpoint.RetryStrategy,
                            cancellationToken,
                            errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                    }
                    context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                    return;
                }

                context.Response.ContentType = Application.Json;

                // Write opening {
                writer.Write(Consts.Utf8OpenBrace);

                bool firstResultWritten = false;
                for (int cmdIndex = 0; cmdIndex < routine.MultiCommandInfo.Length; cmdIndex++)
                {
                    var currentCmd = routine.MultiCommandInfo[cmdIndex];

                    // Build a command for this statement
                    await using var mcCmd = new NpgsqlCommand(currentCmd.Statement, connection);
                    if (endpoint.CommandTimeout.HasValue)
                    {
                        mcCmd.CommandTimeout = endpoint.CommandTimeout.Value.Seconds;
                    }
                    // Bind parameters that this statement uses
                    for (int pi = 0; pi < Math.Min(currentCmd.ParamCount, command.Parameters.Count); pi++)
                    {
                        var srcParam = command.Parameters[pi];
                        mcCmd.Parameters.Add(new NpgsqlParameter
                        {
                            NpgsqlDbType = srcParam.NpgsqlDbType,
                            Value = srcParam.Value ?? DBNull.Value
                        });
                    }

                    // Skipped commands: execute for side effects, no result key
                    if (currentCmd.IsSkipped)
                    {
                        await mcCmd.ExecuteNonQueryWithRetryAsync(
                            endpoint.RetryStrategy,
                            cancellationToken,
                            errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                        continue;
                    }

                    // Write comma between results
                    if (firstResultWritten)
                    {
                        writer.Write(Consts.Utf8Comma);
                    }
                    firstResultWritten = true;

                    // Write "resultName":
                    var keyJson = string.Concat(currentCmd.JsonName, ":");
                    writer.Advance(Encoding.UTF8.GetBytes(keyJson.AsSpan(), writer.GetSpan(Encoding.UTF8.GetMaxByteCount(keyJson.Length))));

                    if (currentCmd.ColumnCount == 0)
                    {
                        // Void command — execute and write rows affected count
                        var rowsAffected = await mcCmd.ExecuteNonQueryWithRetryAsync(
                            endpoint.RetryStrategy,
                            cancellationToken,
                            errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                        var rowsStr = rowsAffected.ToString();
                        writer.Advance(Encoding.UTF8.GetBytes(rowsStr.AsSpan(), writer.GetSpan(Encoding.UTF8.GetMaxByteCount(rowsStr.Length))));
                    }
                    else
                    {
                        // Query command — read result set as JSON array (or object if @single)
                        // AllResultTypesAreUnknown forces all values to string — same as single-command path
                        mcCmd.AllResultTypesAreUnknown = true;
                        await using var mcReader = await mcCmd.ExecuteReaderWithRetryAsync(
                            endpoint.RetryStrategy,
                            cancellationToken,
                            errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);

                        if (!currentCmd.IsSingle)
                        {
                            writer.Write(Consts.Utf8OpenBracket);
                        }
                        bool mcFirstRow = true;
                        mcRowBuilder = StringBuilderPool.Rent(512);
                        var mcBufferRows = endpoint.BufferRows ?? Options.BufferRows;
                        ulong mcRowCount = 0;

                        // Note: response caching for multi-command is not yet supported
                        // (cache key infrastructure is in the single-command path)

                        // Nested composite column support (per-command)
                        Dictionary<int, (string CompositeColumnName, string FieldName, bool IsFirstField, bool IsLastField, int FieldCount, string JsonCompositeColumnName, string JsonFieldName)>? mcNestedMap = null;
                        if (endpoint.NestedJsonForCompositeTypes == true && routine.CompositeColumnInfo is not null)
                        {
                            mcNestedMap = new();
                            foreach (var (_, ci) in routine.CompositeColumnInfo)
                            {
                                var indices = ci.ExpandedColumnIndices;
                                for (int fi = 0; fi < indices.Length; fi++)
                                {
                                    if (indices[fi] < currentCmd.ColumnCount)
                                    {
                                        mcNestedMap[indices[fi]] = (ci.ConvertedColumnName, ci.FieldNames[fi], fi == 0, fi == indices.Length - 1, indices.Length, PgConverters.SerializeString(ci.ConvertedColumnName), PgConverters.SerializeString(ci.FieldNames[fi]));
                                    }
                                }
                            }
                            if (mcNestedMap.Count == 0) mcNestedMap = null;
                        }
                        mcCompositeBuffer = mcNestedMap is not null ? StringBuilderPool.Rent(256) : null;
                        bool mcCompositeHasValue = false;
                        string? mcCurrentCompName = null;
                        string? mcCurrentJsonCompName = null;

                        while (await mcReader.ReadAsync(cancellationToken))
                        {
                            mcRowCount++;
                            if (!mcFirstRow)
                            {
                                mcRowBuilder.Append(Consts.Comma);
                            }
                            mcFirstRow = false;

                            for (int col = 0; col < currentCmd.ColumnCount; col++)
                            {
                                object value = mcReader.GetValue(col);
                                var raw = (value == DBNull.Value ? "" : (string)value).AsSpan();

                                // Decrypt if needed
                                if (value != DBNull.Value &&
                                    Options.AuthenticationOptions.DefaultDataProtector is not null &&
                                    (endpoint.DecryptAllColumns ||
                                     (endpoint.DecryptColumns is not null &&
                                      col < currentCmd.ColumnNames.Length &&
                                      endpoint.DecryptColumns.Contains(currentCmd.ColumnNames[col]))))
                                {
                                    try
                                    {
                                        raw = Options.AuthenticationOptions.DefaultDataProtector.Unprotect((string)value).AsSpan();
                                    }
                                    catch (Exception decryptEx)
                                    {
                                        Logger?.DecryptColumnFailed(decryptEx.Message);
                                    }
                                }

                                var mcDescriptor = currentCmd.ColumnTypeDescriptors[col];

                                // SQL file composite type column in multi-command
                                if (mcDescriptor.IsCompositeType &&
                                    mcDescriptor.CompositeFieldNames is not null &&
                                    mcDescriptor.CompositeFieldDescriptors is not null)
                                {
                                    if (currentCmd.ReturnsUnnamedSet == false && col == 0)
                                    {
                                        mcRowBuilder.Append(Consts.OpenBrace);
                                    }

                                    if (endpoint.NestedJsonForCompositeTypes == true)
                                    {
                                        if (currentCmd.ReturnsUnnamedSet == false)
                                        {
                                            mcRowBuilder.Append(currentCmd.JsonColumnNames[col]);
                                            mcRowBuilder.Append(Consts.Colon);
                                        }
                                        if (value == DBNull.Value)
                                        {
                                            mcRowBuilder.Append(Consts.Null);
                                        }
                                        else
                                        {
                                            mcRowBuilder.Append(PgTupleToJsonObject(
                                                raw,
                                                mcDescriptor.CompositeFieldNames,
                                                mcDescriptor.CompositeFieldDescriptors));
                                        }
                                    }
                                    else
                                    {
                                        // Flat mode: splice fields inline
                                        if (value == DBNull.Value)
                                        {
                                            for (int fi = 0; fi < mcDescriptor.CompositeFieldNames.Length; fi++)
                                            {
                                                if (fi > 0) mcRowBuilder.Append(Consts.Comma);
                                                mcRowBuilder.Append(PgConverters.SerializeString(mcDescriptor.CompositeFieldNames[fi]));
                                                mcRowBuilder.Append(Consts.Colon);
                                                mcRowBuilder.Append(Consts.Null);
                                            }
                                        }
                                        else
                                        {
                                            var compositeJsonObj = PgTupleToJsonObject(
                                                raw,
                                                mcDescriptor.CompositeFieldNames,
                                                mcDescriptor.CompositeFieldDescriptors);
                                            if (compositeJsonObj.Length >= 2 && compositeJsonObj[0] == '{' && compositeJsonObj[compositeJsonObj.Length - 1] == '}')
                                            {
                                                mcRowBuilder.Append(compositeJsonObj[1..^1]);
                                            }
                                            else
                                            {
                                                mcRowBuilder.Append(compositeJsonObj);
                                            }
                                        }
                                    }

                                    if (currentCmd.ReturnsUnnamedSet == false && col == currentCmd.ColumnCount - 1)
                                    {
                                        mcRowBuilder.Append(Consts.CloseBrace);
                                    }
                                    if (col < currentCmd.ColumnCount - 1)
                                    {
                                        mcRowBuilder.Append(Consts.Comma);
                                    }
                                }
                                else
                                {
                                // Determine output buffer and handle composite nesting
                                (string CompositeColumnName, string FieldName, bool IsFirstField, bool IsLastField, int FieldCount, string JsonCompositeColumnName, string JsonFieldName) mcCompMapping = default;
                                bool mcIsInComposite = mcNestedMap is not null && mcNestedMap.TryGetValue(col, out mcCompMapping);
                                StringBuilder mcOutput = (mcIsInComposite && mcCompositeBuffer is not null) ? mcCompositeBuffer : mcRowBuilder;

                                // Column name / structure
                                if (currentCmd.ReturnsUnnamedSet == false)
                                {
                                    if (col == 0) mcRowBuilder.Append(Consts.OpenBrace);

                                    if (mcIsInComposite)
                                    {
                                        if (mcCompMapping.IsFirstField)
                                        {
                                            mcCompositeBuffer!.Clear();
                                            mcCompositeHasValue = false;
                                            mcCurrentCompName = mcCompMapping.CompositeColumnName;
                                            mcCurrentJsonCompName = mcCompMapping.JsonCompositeColumnName;
                                        }
                                        mcOutput.Append(mcCompMapping.JsonFieldName);
                                        mcOutput.Append(Consts.Colon);
                                    }
                                    else
                                    {
                                        mcRowBuilder.Append(currentCmd.JsonColumnNames[col]);
                                        mcRowBuilder.Append(Consts.Colon);
                                    }
                                }

                                // Value formatting — shared with single-command path
                                JsonValueFormatter.FormatValue(
                                    raw, value,
                                    mcDescriptor,
                                    mcOutput,
                                    routine.ArrayCompositeColumnInfo,
                                    col);

                                if (mcIsInComposite && value != DBNull.Value) mcCompositeHasValue = true;

                                // Composite field closing
                                if (mcIsInComposite && mcCompMapping.IsLastField)
                                {
                                    mcRowBuilder.Append(mcCurrentJsonCompName!);
                                    mcRowBuilder.Append(Consts.Colon);
                                    if (mcCompositeHasValue)
                                    {
                                        mcRowBuilder.Append(Consts.OpenBrace);
                                        mcRowBuilder.Append(mcCompositeBuffer);
                                        mcRowBuilder.Append(Consts.CloseBrace);
                                    }
                                    else
                                    {
                                        mcRowBuilder.Append(Consts.Null);
                                    }
                                }
                                else if (mcIsInComposite)
                                {
                                    mcOutput.Append(Consts.Comma);
                                }

                                // Close brace and commas
                                if (currentCmd.ReturnsUnnamedSet == false && col == currentCmd.ColumnCount - 1)
                                {
                                    mcRowBuilder.Append(Consts.CloseBrace);
                                }
                                if (!mcIsInComposite && col < currentCmd.ColumnCount - 1)
                                {
                                    mcRowBuilder.Append(Consts.Comma);
                                }
                                else if (mcIsInComposite && mcCompMapping.IsLastField && col < currentCmd.ColumnCount - 1)
                                {
                                    mcRowBuilder.Append(Consts.Comma);
                                }
                                } // end non-composite mc else
                            }

                            if (currentCmd.IsSingle)
                            {
                                break;
                            }

                            // Buffer rows flush
                            if (mcBufferRows > 0 && mcRowCount % mcBufferRows == 0)
                            {
                                WriteStringBuilderToWriter(mcRowBuilder, writer);
                                await writer.FlushAsync(cancellationToken);
                            }
                        }

                        if (currentCmd.IsSingle && mcRowCount == 0)
                        {
                            // Empty result with @single — write null
                            writer.Write(Consts.Utf8Null);
                        }

                        if (mcRowBuilder.Length > 0)
                        {
                            WriteStringBuilderToWriter(mcRowBuilder, writer);
                        }
                        StringBuilderPool.Return(mcRowBuilder);
                        mcRowBuilder = null;
                        if (mcCompositeBuffer is not null)
                        {
                            StringBuilderPool.Return(mcCompositeBuffer);
                            mcCompositeBuffer = null;
                        }

                        if (!currentCmd.IsSingle)
                        {
                            writer.Write(Consts.Utf8CloseBracket);
                        }
                    }
                }

                // Write closing }
                writer.Write(Consts.Utf8CloseBrace);
                await writer.FlushAsync(cancellationToken);
                return;
            }

            if (routine.IsVoid || endpoint.Void)
            {
                if (await PrepareCommand(connection, command, commandText, context, endpoint, true, cancellationToken) is false)
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
                    if (resolvedCache is not null && endpoint.Cached is true && bypassCache is false)
                    {
                        if (resolvedCache.Get(endpoint, cacheKeyString!, out valueResult) is false)
                        {
                            if (await PrepareCommand(connection, command, commandText, context, endpoint, true, cancellationToken) is false)
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
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                valueResult = descriptor.IsBinary ? reader.GetFieldValue<byte[]>(0) : reader.GetValue(0) as string;
                                resolvedCache.AddOrUpdate(endpoint, cacheKeyString!, valueResult, cacheTtlOverride);
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
                        if (await PrepareCommand(connection, command, commandText, context, endpoint, true, cancellationToken) is false)
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
                        if (await reader.ReadAsync(cancellationToken))
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

                    // Decrypt scalar result if marked with decrypt annotation
                    if (valueResult is string scalarStr &&
                        Options.AuthenticationOptions.DefaultDataProtector is not null &&
                        (endpoint.DecryptAllColumns || endpoint.DecryptColumns is not null))
                    {
                        try
                        {
                            valueResult = Options.AuthenticationOptions.DefaultDataProtector.Unprotect(scalarStr);
                        }
                        catch (Exception decryptEx)
                        {
                            Logger?.DecryptColumnFailed(decryptEx.Message);
                        }
                    }

                    if (context.Response.ContentType is null)
                    {
                        if (descriptor.IsBinary)
                        {
                            context.Response.ContentType = Application.Octet;
                        }
                        else if ((descriptor.Category & TypeCategory.Json) != 0 || descriptor.IsArray)
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
                            await writer.WriteAsync(valueResult as byte[], cancellationToken);
                            await writer.FlushAsync(cancellationToken);
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
                                writer.Write(Consts.Utf8Null);
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
                            await writer.WriteAsync(valueResult as byte[], cancellationToken);
                            await writer.FlushAsync(cancellationToken);
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
                    var canCacheRecordsAndSets = resolvedCache is not null
                        && endpoint.Cached is true
                        && bypassCache is false
                        && binary is false
                        && endpoint.Raw is false;

                    if (canCacheRecordsAndSets)
                    {
                        if (resolvedCache!.Get(endpoint, cacheKeyString!, out var cachedResult))
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

                    NpgsqlDataReader reader;
                    await using NpgsqlBatch? batch = routine.IsMultiCommand && routine.MultiCommandInfo is not null ? new NpgsqlBatch(connection) : null;

                    if (batch is not null)
                    {
                        // Multi-command: use NpgsqlBatch with one command per statement
                        foreach (var cmdInfo in routine.MultiCommandInfo!)
                        {
                            var batchCmd = new NpgsqlBatchCommand(cmdInfo.Statement);
                            // Copy bound parameters from the main command to each batch command
                            for (int pi = 0; pi < Math.Min(command.Parameters.Count, cmdInfo.ParamCount); pi++)
                            {
                                var srcParam = command.Parameters[pi];
                                var batchParam = NpgsqlRestParameter.CreateParamWithType(srcParam.NpgsqlDbType);
                                batchParam.Value = srcParam.Value ?? DBNull.Value;
                                batchCmd.Parameters.Add(batchParam);
                            }
                            batch.BatchCommands.Add(batchCmd);
                        }
                        reader = await batch.ExecuteBatchReaderWithRetryAsync(
                            CommandBehavior.SequentialAccess,
                            endpoint.RetryStrategy,
                            cancellationToken,
                            errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                        if (shouldLog)
                        {
                            NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", string.Join("; ", routine.MultiCommandInfo.Select(c => c.Statement)));
                        }
                    }
                    else
                    {
                        // Single command: use NpgsqlCommand as before
                        if (await PrepareCommand(connection, command, commandText, context, endpoint, true, cancellationToken) is false)
                        {
                            return;
                        }
                        reader = await command.ExecuteReaderWithRetryAsync(
                            CommandBehavior.SequentialAccess,
                            endpoint.RetryStrategy,
                            cancellationToken,
                            errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                        if (shouldLog)
                        {
                            NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                        }
                    }
                    await using var _ = reader; // ensure disposal

                    // Pluggable table format renderer
                    if (Options.TableFormatHandlers is not null
                        && customParameters is not null
                        && customParameters.TryGetValue("table_format", out var tableFormatName)
                        && Options.TableFormatHandlers.TryGetValue(tableFormatName, out var tableFormatHandler))
                    {
                        context.Response.ContentType = tableFormatHandler.ContentType;
                        var tfBufferRows = endpoint.BufferRows ?? Options.BufferRows;
                        if (routine.IsMultiCommand && routine.MultiCommandInfo is not null)
                        {
                            // Multi-command: call handler per result set
                            var tfCmdIndex = 0;
                            do
                            {
                                await tableFormatHandler.RenderAsync(reader, routine, endpoint, writer, context, tfBufferRows, customParameters, cancellationToken);
                                tfCmdIndex++;
                            } while (tfCmdIndex < routine.MultiCommandInfo.Length && await reader.NextResultAsync(cancellationToken));
                        }
                        else
                        {
                            await tableFormatHandler.RenderAsync(reader, routine, endpoint, writer, context, tfBufferRows, customParameters, cancellationToken);
                        }
                        await writer.FlushAsync(cancellationToken);
                        return;
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

                    // Multi-command rendering: wrap result sets in JSON object (not in raw/binary mode)
                    var multiCmd = routine.MultiCommandInfo;
                    int multiCmdIndex = 0;
                    bool multiCmdFirstWritten = false;
                    bool multiCmdWriteWrapper = multiCmd is not null && binary is false && endpoint.Raw is false;
                    if (multiCmdWriteWrapper)
                    {
                        writer.Write(Consts.Utf8OpenBrace);
                    }

                    // Begin result set loop (single pass for normal, do/while for multi-command)
                    do
                    {
                    // For multi-command: determine current command metadata and skip void commands
                    string[]? currentColumnNames = null;
                    string[]? currentJsonColumnNames = null;
                    TypeDescriptor[]? currentColumnDescriptors = null;
                    int currentColumnCount = routine.ColumnCount;
                    if (multiCmd is not null && multiCmdIndex < multiCmd.Length)
                    {
                        var cmdInfo = multiCmd[multiCmdIndex];

                        if (multiCmdWriteWrapper)
                        {
                            // Write command name key in JSON wrapper
                            if (multiCmdFirstWritten)
                            {
                                writer.Write(Consts.Utf8Comma);
                            }
                            var nameJson = string.Concat(cmdInfo.JsonName, ":");
                            writer.Advance(Encoding.UTF8.GetBytes(nameJson.AsSpan(), writer.GetSpan(Encoding.UTF8.GetMaxByteCount(nameJson.Length))));
                            multiCmdFirstWritten = true;
                        }

                        if (cmdInfo.ColumnCount == 0)
                        {
                            if (multiCmdWriteWrapper)
                            {
                                // Void command — write rows-affected count
                                var rowsAffected = reader.RecordsAffected;
                                var rowsStr = rowsAffected.ToString();
                                writer.Advance(Encoding.UTF8.GetBytes(rowsStr.AsSpan(), writer.GetSpan(Encoding.UTF8.GetMaxByteCount(rowsStr.Length))));
                            }
                            multiCmdIndex++;
                            continue;
                        }

                        currentColumnNames = cmdInfo.ColumnNames;
                        currentJsonColumnNames = cmdInfo.JsonColumnNames;
                        currentColumnDescriptors = cmdInfo.ColumnTypeDescriptors;
                        currentColumnCount = cmdInfo.ColumnCount;
                    }

                    // Per-command single record: for multi-command, use per-command IsSingle only;
                    // for single-command, use endpoint-level ReturnSingleRecord
                    var isSingleRecord = multiCmd is not null
                        ? (multiCmdIndex < multiCmd.Length && multiCmd[multiCmdIndex].IsSingle)
                        : endpoint.ReturnSingleRecord;




                    if (routine.ReturnsSet && endpoint.Raw is false && binary is false && isSingleRecord is false)
                    {
                        writer.Write(Consts.Utf8OpenBracket);
                        if (shouldCache)
                        {
                            cacheBuffer!.Append(Consts.OpenBracket);
                        }
                    }

                    bool first = true;
                    var routineReturnRecordCount = currentColumnCount;
                    var activeColumnNames = currentColumnNames ?? routine.ColumnNames;
                    var activeJsonColumnNames = currentJsonColumnNames ?? routine.JsonColumnNames;
                    var activeColumnDescriptors = currentColumnDescriptors ?? routine.ColumnsTypeDescriptor;

                    rowBuilder = StringBuilderPool.Rent(512);
                    ulong rowCount = 0;

                    // Precompute nested JSON composite column mapping
                    // Maps column index to: (compositeColumnName, fieldName, isFirstField, isLastField, fieldCount, jsonCompositeColumnName, jsonFieldName)
                    Dictionary<int, (string CompositeColumnName, string FieldName, bool IsFirstField, bool IsLastField, int FieldCount, string JsonCompositeColumnName, string JsonFieldName)>? nestedJsonColumnMap = null;
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
                                    fieldCount,
                                    PgConverters.SerializeString(compositeInfo.ConvertedColumnName),
                                    PgConverters.SerializeString(compositeInfo.FieldNames[fieldIdx])
                                );
                            }
                        }
                    }

                    // Buffer for composite fields to detect all-NULL composites
                    if (nestedJsonColumnMap is not null)
                    {
                        compositeFieldBuffer = StringBuilderPool.Rent(256);
                    }
                    bool compositeHasNonNullValue = false;
                    string? currentCompositeName = null;
                    string? currentJsonCompositeName = null;

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
                        rowBuilder.Append(columns);
                    }

                    var bufferRows = endpoint.BufferRows ?? Options.BufferRows;
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        rowCount++;
                        if (!first)
                        {
                            if (binary is false)
                            {
                                // if raw
                                if (endpoint.Raw is false)
                                {
                                    rowBuilder.Append(Consts.Comma);
                                }
                                else if (endpoint.RawNewLineSeparator is not null)
                                {
                                    rowBuilder.Append(endpoint.RawNewLineSeparator);
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
                                await writer.WriteAsync(reader.GetFieldValue<byte[]>(0), cancellationToken);
                            }
                            else
                            {
                                object value = reader.GetValue(i);
                                // AllResultTypesAreUnknown = true always returns string, except for null
                                var raw = (value == DBNull.Value ? "" : (string)value).AsSpan();

                                // Decrypt column value if marked with decrypt annotation
                                if (value != DBNull.Value &&
                                    Options.AuthenticationOptions.DefaultDataProtector is not null &&
                                    (endpoint.DecryptAllColumns ||
                                     (endpoint.DecryptColumns is not null &&
                                      i < activeColumnNames.Length &&
                                      endpoint.DecryptColumns.Contains(activeColumnNames[i]))))
                                {
                                    try
                                    {
                                        raw = Options.AuthenticationOptions.DefaultDataProtector.Unprotect((string)value).AsSpan();
                                    }
                                    catch (Exception decryptEx)
                                    {
                                        Logger?.DecryptColumnFailed(decryptEx.Message);
                                    }
                                }

                                // if raw
                                if (endpoint.Raw)
                                {
                                    if (endpoint.RawValueSeparator is not null)
                                    {
                                        var descriptor = activeColumnDescriptors[i];
                                        if ((descriptor.Category & (TypeCategory.Text | TypeCategory.Date | TypeCategory.DateTime)) != 0)
                                        {
                                            rowBuilder.Append(QuoteText(raw));
                                        }
                                        else
                                        {
                                            rowBuilder.Append(raw);
                                        }
                                        if (i < routineReturnRecordCount - 1)
                                        {
                                            rowBuilder.Append(endpoint.RawValueSeparator);
                                        }
                                    }
                                    else
                                    {
                                        rowBuilder.Append(raw);
                                    }
                                }
                                else
                                {
                                    var descriptor = activeColumnDescriptors[i];

                                    // SQL file composite type column: value is a PostgreSQL tuple string
                                    // Expand inline (flat) or wrap as nested JSON object
                                    if (descriptor.IsCompositeType &&
                                        descriptor.CompositeFieldNames is not null &&
                                        descriptor.CompositeFieldDescriptors is not null)
                                    {
                                        if (routine.ReturnsUnnamedSet == false && i == 0)
                                        {
                                            rowBuilder.Append(Consts.OpenBrace);
                                        }

                                        if (endpoint.NestedJsonForCompositeTypes == true)
                                        {
                                            // Nested mode: "columnName": {parsed object} or "columnName": null
                                            if (routine.ReturnsUnnamedSet == false)
                                            {
                                                rowBuilder.Append(activeJsonColumnNames[i]);
                                                rowBuilder.Append(Consts.Colon);
                                            }
                                            if (value == DBNull.Value)
                                            {
                                                rowBuilder.Append(Consts.Null);
                                            }
                                            else
                                            {
                                                rowBuilder.Append(PgTupleToJsonObject(
                                                    raw,
                                                    descriptor.CompositeFieldNames,
                                                    descriptor.CompositeFieldDescriptors));
                                            }
                                        }
                                        else
                                        {
                                            // Flat mode: splice individual fields inline
                                            if (value == DBNull.Value)
                                            {
                                                // All fields null — emit "field1":null,"field2":null,...
                                                for (int fi = 0; fi < descriptor.CompositeFieldNames.Length; fi++)
                                                {
                                                    if (fi > 0) rowBuilder.Append(Consts.Comma);
                                                    rowBuilder.Append(PgConverters.SerializeString(descriptor.CompositeFieldNames[fi]));
                                                    rowBuilder.Append(Consts.Colon);
                                                    rowBuilder.Append(Consts.Null);
                                                }
                                            }
                                            else
                                            {
                                                // Parse tuple to JSON object, strip outer braces to get inline fields
                                                var compositeJsonObj = PgTupleToJsonObject(
                                                    raw,
                                                    descriptor.CompositeFieldNames,
                                                    descriptor.CompositeFieldDescriptors);
                                                // PgTupleToJsonObject returns {"field":val,...} — strip { and }
                                                if (compositeJsonObj.Length >= 2 && compositeJsonObj[0] == '{' && compositeJsonObj[compositeJsonObj.Length - 1] == '}')
                                                {
                                                    rowBuilder.Append(compositeJsonObj[1..^1]);
                                                }
                                                else
                                                {
                                                    rowBuilder.Append(compositeJsonObj);
                                                }
                                            }
                                        }

                                        if (routine.ReturnsUnnamedSet == false && i == routineReturnRecordCount - 1)
                                        {
                                            rowBuilder.Append(Consts.CloseBrace);
                                        }
                                        if (i < routineReturnRecordCount - 1)
                                        {
                                            rowBuilder.Append(Consts.Comma);
                                        }
                                    }
                                    else
                                    {
                                    // Handle nested JSON composite types (routine expanded columns)
                                    (string CompositeColumnName, string FieldName, bool IsFirstField, bool IsLastField, int FieldCount, string JsonCompositeColumnName, string JsonFieldName) compositeMapping = default;
                                    bool isInComposite = nestedJsonColumnMap is not null && nestedJsonColumnMap.TryGetValue(i, out compositeMapping);

                                    // Determine which buffer to write to
                                    StringBuilder outputBuffer = (isInComposite && compositeFieldBuffer is not null) ? compositeFieldBuffer : rowBuilder;

                                    if (routine.ReturnsUnnamedSet == false)
                                    {
                                        if (i == 0)
                                        {
                                            rowBuilder.Append(Consts.OpenBrace);
                                        }

                                        if (isInComposite)
                                        {
                                            if (compositeMapping.IsFirstField)
                                            {
                                                // Start of composite: reset buffer and track column name
                                                compositeFieldBuffer!.Clear();
                                                compositeHasNonNullValue = false;
                                                currentCompositeName = compositeMapping.CompositeColumnName;
                                                currentJsonCompositeName = compositeMapping.JsonCompositeColumnName;

                                                // Write field name/value to buffer
                                                outputBuffer.Append(compositeMapping.JsonFieldName);
                                                outputBuffer.Append(Consts.Colon);
                                            }
                                            else
                                            {
                                                // Middle or end field in composite: just output field name
                                                outputBuffer.Append(compositeMapping.JsonFieldName);
                                                outputBuffer.Append(Consts.Colon);
                                            }
                                        }
                                        else
                                        {
                                            rowBuilder.Append(activeJsonColumnNames[i]);
                                            rowBuilder.Append(Consts.Colon);
                                        }
                                    }

                                    JsonValueFormatter.FormatValue(
                                        raw, value, descriptor, outputBuffer,
                                        routine.ArrayCompositeColumnInfo, i);
                                    if (isInComposite && value != DBNull.Value)
                                    {
                                        compositeHasNonNullValue = true;
                                    }

                                    // Handle closing braces and commas for nested JSON composite types
                                    if (isInComposite && compositeMapping.IsLastField)
                                    {
                                        // End of composite: decide whether to output null or the buffered object
                                        rowBuilder.Append(currentJsonCompositeName!);
                                        rowBuilder.Append(Consts.Colon);

                                        if (compositeHasNonNullValue)
                                        {
                                            // At least one field has a value, output as object
                                            rowBuilder.Append(Consts.OpenBrace);
                                            rowBuilder.Append(compositeFieldBuffer);
                                            rowBuilder.Append(Consts.CloseBrace);
                                        }
                                        else
                                        {
                                            // All fields are NULL, output null
                                            rowBuilder.Append(Consts.Null);
                                        }
                                    }
                                    else if (isInComposite)
                                    {
                                        // Add comma between composite fields
                                        outputBuffer.Append(Consts.Comma);
                                    }

                                    if (routine.ReturnsUnnamedSet == false && i == routineReturnRecordCount - 1)
                                    {
                                        rowBuilder.Append(Consts.CloseBrace);
                                    }

                                    // Add comma between columns (but not within composite fields which are handled above)
                                    if (!isInComposite && i < routineReturnRecordCount - 1)
                                    {
                                        rowBuilder.Append(Consts.Comma);
                                    }
                                    else if (isInComposite && compositeMapping.IsLastField && i < routineReturnRecordCount - 1)
                                    {
                                        rowBuilder.Append(Consts.Comma);
                                    }
                                    } // end non-composite-type else
                                }
                            }
                        } // end for

                        // Check if we've exceeded the cacheable row limit
                        if (shouldCache && maxCacheableRows.HasValue && rowCount > (ulong)maxCacheableRows.Value)
                        {
                            shouldCache = false;
                            cacheBuffer = null; // Release memory
                        }

                        if (isSingleRecord)
                        {
                            break;
                        }

                        if (bufferRows != 1 && rowCount % bufferRows == 0)
                        {
                            // Append to cache buffer before clearing row
                            if (shouldCache)
                            {
                                cacheBuffer!.Append(rowBuilder);
                            }
                            WriteStringBuilderToWriter(rowBuilder, writer);
                            await writer.FlushAsync(cancellationToken);
                            rowBuilder.Clear();
                        }
                    } // end while

                    if (isSingleRecord && rowCount == 0)
                    {
                        if (multiCmd is not null)
                        {
                            // Multi-command: always write null for empty @single results
                            writer.Write(Consts.Utf8Null);
                        }
                        else if (endpoint.TextResponseNullHandling == TextResponseNullHandling.NullLiteral)
                        {
                            writer.Write(Consts.Utf8Null);
                            await writer.FlushAsync(cancellationToken);
                        }
                        else if (endpoint.TextResponseNullHandling == TextResponseNullHandling.NoContent)
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                        }
                        // else EmptyString: empty 200 OK (default)
                    }
                    else if (binary is true)
                    {
                        await writer.FlushAsync(cancellationToken);
                    }
                    else
                    {
                        if (rowBuilder.Length > 0)
                        {
                            // Append remaining rows to cache buffer
                            if (shouldCache)
                            {
                                cacheBuffer!.Append(rowBuilder);
                            }
                            WriteStringBuilderToWriter(rowBuilder, writer);
                            await writer.FlushAsync(cancellationToken);
                        }
                        if (routine.ReturnsSet && endpoint.Raw is false && isSingleRecord is false)
                        {
                            writer.Write(Consts.Utf8CloseBracket);
                            if (shouldCache)
                            {
                                cacheBuffer!.Append(Consts.CloseBracket);
                            }
                        }

                        // Store in cache if within limits
                        if (shouldCache && cacheBuffer is not null)
                        {
                            resolvedCache?.AddOrUpdate(endpoint, cacheKeyString!, cacheBuffer.ToString(), cacheTtlOverride);
                        }
                    }

                    multiCmdIndex++;
                    } while (multiCmd is not null && multiCmdIndex < multiCmd.Length && await reader.NextResultAsync(cancellationToken));

                    // Close multi-command JSON object
                    if (multiCmdWriteWrapper)
                    {
                        writer.Write(Consts.Utf8CloseBrace);
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

            if (context.Response.StatusCode == 400)
            {
                if (shouldLog && cmdLog is not null)
                {
                    Logger?.LogWarning("Client error (400) executing command: {commandText} mapped to endpoint: {Url}: {message}{NewLine}{cmdLog}", commandText, endpoint.Path, exception.Message, Environment.NewLine, cmdLog.ToString());
                }
                else
                {
                    Logger?.LogWarning("Client error (400) executing command: {commandText} mapped to endpoint: {Url}: {message}", commandText, endpoint.Path, exception.Message);
                }
            }
            else if (context.Response.StatusCode != 200 && context.Response.StatusCode != 205)
            {
                if (shouldLog && cmdLog is not null)
                {
                    Logger?.LogError(exception, "Error executing command: {commandText} mapped to endpoint: {Url}{NewLine}{cmdLog}", commandText, endpoint.Path, Environment.NewLine, cmdLog.ToString());
                }
                else
                {
                    Logger?.LogError(exception, "Error executing command: {commandText} mapped to endpoint: {Url}", commandText, endpoint.Path);
                }
            }
        }
        finally
        {
            await writer.CompleteAsync();

            // proxy_out: capture buffered function output and forward to upstream proxy
            if (proxyOutBuffer is not null && proxyOutOriginalBody is not null)
            {
                try
                {
                    // Only forward to proxy if the function executed successfully (no error status)
                    if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 400)
                    {
                        // Extract bytes directly — avoid string allocation and double UTF-8 conversion
                        var functionBodyBytes = proxyOutBuffer.Length > 0
                            ? proxyOutBuffer.ToArray()
                            : [];

                        // Restore original response body and reset response state
                        context.Response.Body = proxyOutOriginalBody;
                        context.Response.Headers.Clear();
                        context.Response.StatusCode = 200;

                        var proxyResponse = await Proxy.ProxyRequestHandler.InvokeOutAsync(
                            context, endpoint, functionBodyBytes, cancellationToken);
                        await Proxy.ProxyRequestHandler.WriteResponseAsync(
                            context, proxyResponse, Options.ProxyOptions, cancellationToken);
                    }
                    else
                    {
                        // Function errored — restore original body and copy error content through
                        proxyOutBuffer.Position = 0;
                        context.Response.Body = proxyOutOriginalBody;
                        await proxyOutBuffer.CopyToAsync(proxyOutOriginalBody, cancellationToken);
                    }
                }
                finally
                {
                    proxyOutBuffer.Dispose();
                }
            }

            await context.Response.CompleteAsync();
            if (transaction is not null)
            {
                if (connection is not null && connection.State == ConnectionState.Open)
                {
                    if (shouldCommit)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }
                }
            }
            if (connection is not null && shouldDispose is true)
            {
                await connection.DisposeAsync();
            }

            // Return pooled StringBuilders
            if (cmdLog is not null)
            {
                StringBuilderPool.Return(cmdLog);
            }
            if (cacheKeys is not null)
            {
                StringBuilderPool.Return(cacheKeys);
            }
            if (commandTextBuilder is not null)
            {
                StringBuilderPool.Return(commandTextBuilder);
            }
            if (rowBuilder is not null)
            {
                StringBuilderPool.Return(rowBuilder);
            }
            if (compositeFieldBuffer is not null)
            {
                StringBuilderPool.Return(compositeFieldBuffer);
            }
            if (mcRowBuilder is not null)
            {
                StringBuilderPool.Return(mcRowBuilder);
            }
            if (mcCompositeBuffer is not null)
            {
                StringBuilderPool.Return(mcCompositeBuffer);
            }
        }
    }
}