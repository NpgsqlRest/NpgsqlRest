using System.Buffers;
using System.Collections.Concurrent;
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
    // Signals a non-value exit from inside a cache GetOrCreateAsync factory, so the result is never
    // cached and concurrent waiters observe the same outcome. NoRow → the command returned no row
    // (caller maps to 500); ResponseAlreadyWritten → PrepareCommand short-circuited via
    // CommandCallbackAsync and already wrote the lead caller's response (caller just returns).
    private enum CacheFactoryAbortReason { NoRow, ResponseAlreadyWritten }

    private sealed class CacheFactoryAbortException(CacheFactoryAbortReason reason) : Exception
    {
        public CacheFactoryAbortReason Reason { get; } = reason;
    }

    // Per-cache-key execution gates for the streaming records/sets path. That path cannot use the
    // value-shaped GetOrCreateAsync factory model because it streams rows to the client and disables
    // caching mid-stream once a response exceeds MaxCacheableRows. The gate serializes concurrent
    // requests for the same key: the lead executes and (when within limits) populates the cache, so
    // the rest get a cache hit instead of re-executing; over-limit responses simply run one-at-a-time,
    // which still caps concurrent DB executions per key at one.
    //
    // The outer map is keyed by the cache instance itself (reference identity, so distinct cache
    // profiles never share a gate even if they reuse a key string) and an inner map holds one gate per
    // key. A gate exists only while requests hold or wait for it: it is reference-counted and removed by
    // the last one to leave. (Gates used to stay forever - one per distinct key ever requested, and keys
    // contain request values, so callers could grow the map without limit.)
    private static readonly ConcurrentDictionary<IRoutineCache, ConcurrentDictionary<string, RecordCacheGate>> _recordCacheGates
        = new(ReferenceEqualityComparer.Instance);

    internal static int RecordCacheGateCount => _recordCacheGates.Values.Sum(static g => g.Count);

    private sealed class RecordCacheGate(ConcurrentDictionary<string, RecordCacheGate> owner, string key)
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private int _users; // requests holding or waiting; -1 = retired (removed from owner). Guarded by lock(this).

        private static RecordCacheGate Enter(IRoutineCache cache, string key)
        {
            var gates = _recordCacheGates.GetOrAdd(cache, static _ => new(StringComparer.Ordinal));
            while (true)
            {
                var gate = gates.GetOrAdd(key, static (k, g) => new RecordCacheGate(g, k), gates);
                lock (gate)
                {
                    if (gate._users >= 0)
                    {
                        gate._users++;
                        return gate;
                    }
                }
                // retired by its last user between GetOrAdd and lock: take (or create) the current one
            }
        }

        /// <summary>
        /// Enters the gate for <paramref name="key"/> and waits for it. If the wait is cancelled, the gate is left
        /// without releasing (it was never acquired) and the cancellation propagates.
        /// </summary>
        public static async Task<RecordCacheGate> AcquireAsync(IRoutineCache cache, string key, CancellationToken cancellationToken)
        {
            var gate = Enter(cache, key);
            try
            {
                await gate._semaphore.WaitAsync(cancellationToken);
            }
            catch
            {
                gate.Leave(acquired: false);
                throw;
            }
            return gate;
        }

        public void Leave(bool acquired = true)
        {
            if (acquired)
            {
                _semaphore.Release();
            }
            lock (this)
            {
                if (--_users == 0)
                {
                    _users = -1;
                    owner.TryRemove(new KeyValuePair<string, RecordCacheGate>(key, this));
                }
            }
        }
    }

    public Task InvokeAsync(HttpContext context, IServiceProvider? serviceProvider, CancellationToken cancellationToken = default)
        => RunAsync(context, serviceProvider, cancellationToken).AsTask();

    /// <summary>Route handler (see NpgsqlRestBuilder): the service provider is resolved from the request only when needed.</summary>
    internal Task HandleRequestAsync(HttpContext context, CancellationToken cancellationToken)
        => RunAsync(context, null, cancellationToken).AsTask();

    /// <summary>The route handler as a RequestDelegate: what the endpoint calls when it has no endpoint filters (see NpgsqlRestBuilder).</summary>
    internal Task HandleDirectAsync(HttpContext context)
        => RunAsync(context, null, context.RequestAborted).AsTask();

    /// <summary>
    /// A request, start to end. This method only sequences the stages: everything a request needs lives in one pooled
    /// <see cref="RequestState"/>, so while the request waits for the database only a few small async frames are
    /// suspended (this one holds the state and nothing else).
    /// </summary>
    // Pooled builder (as on the stage methods below): without Runtime Async, the state machine box of a method that
    // waits on I/O is reused across requests instead of allocated; AsTask() above then allocates only a small Task.
    // With Runtime Async the attribute has no effect - the runtime keeps a method's state itself.
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask RunAsync(HttpContext context, IServiceProvider? serviceProvider, CancellationToken cancellationToken)
    {
        // ASP.NET Core's antiforgery middleware (UseAntiforgery) only *records* a failed token validation in
        // IAntiforgeryValidationFeature and continues - rejecting the request is left to the endpoint (MVC and
        // minimal-API form binding do it). So when the endpoint carries antiforgery metadata and the token was
        // missing or invalid, the request is rejected here, before anything runs.
        if (context.Features.Get<Microsoft.AspNetCore.Antiforgery.IAntiforgeryValidationFeature>() is { IsValid: false })
        {
            await Results.Problem(
                type: null,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Invalid or missing antiforgery token.").ExecuteAsync(context);
            return;
        }

        var s = RequestState.Rent();
        s.context = context;
        s.cancellationToken = cancellationToken;
        s.serviceProvider = serviceProvider;
        s.endpoint = entry.Endpoint;
        s.routine = entry.Endpoint.Routine;
        s.formatter = entry.Formatter;
        s.shouldLog = Options.LogCommands && Logger != null;
        if (s.endpoint.RequestHeadersMode == RequestHeadersMode.Context)
        {
            SearializeHeader(Options, context, ref s.headers);
        }

        // proxy_out: capture function output into a buffer instead of writing to the real response
        if (s.endpoint.IsProxyOut && Options.ProxyOptions.Enabled)
        {
            s.proxyOutBuffer = new MemoryStream();
            s.proxyOutOriginalBody = context.Response.Body;
            context.Response.Body = s.proxyOutBuffer;
        }

        // The response's own pipe: Response.BodyWriter. (Wrapping Response.Body in a new StreamPipeWriter allocated a
        // writer per request and copied every byte once more through the stream into the same pipe.) BodyWriter stays
        // consistent with Response.Body when middleware replaces it (e.g. response compression).
        s.writer = s.proxyOutBuffer is not null
            ? PipeWriter.Create(context.Response.Body, new StreamPipeWriterOptions(leaveOpen: true))
            : context.Response.BodyWriter;
        try
        {
            // A stage returns false when the request is finished: a response was written or its status set.
            if (await BeginRequestAsync(s) is false || await PrepareParametersAsync(s) is false)
            {
                return;
            }
            if (s.endpoint.IsProxy && Options.ProxyOptions.Enabled && await ProxyAsync(s) is false)
            {
                return;
            }
            await SetRequestContextAsync(s);
            if (s.endpoint.Upload is true && s.uploadHandler is not null)
            {
                await UploadAsync(s);
            }
            if ((s.endpoint.Login is true || s.endpoint.Logout is true || s.endpoint.InvalidateCache is true)
                && await LoginLogoutOrInvalidateAsync(s) is false)
            {
                return;
            }

            // The routine runs and its result is rendered: one method per shape, so each keeps little state alive
            // while it waits for the database.
            if (s.routine.IsMultiCommand && s.routine.MultiCommandInfo is not null)
            {
                await RenderMultiCommandAsync(s);
            }
            else if (s.routine.IsVoid || s.endpoint.Void)
            {
                await RenderVoidAsync(s);
            }
            else if (s.routine.ReturnsSet == false && s.routine.ColumnCount == 1 && s.routine.ReturnsRecordType is false)
            {
                if (s.resolvedCache is not null && s.endpoint.Cached is true && s.bypassCache is false)
                {
                    await RenderCachedScalarAsync(s);
                }
                else
                {
                    await RenderScalarAsync(s);
                }
            }
            else
            {
                await RenderRecordsAsync(s);
            }
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(s, exception);
        }
        finally
        {
            try
            {
                await CompleteRequestAsync(s);
            }
            finally
            {
                RequestState.Return(s);
            }
        }
    }

    /// <summary>
    /// First stage of a request: the connection, the notice handler, upfront authentication and authorization, the
    /// command, the request body and parameter binding. Returns false when the request is finished (a response was
    /// written or its status set).
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> BeginRequestAsync(RequestState s)
    {
        var context = s.context;
        var cancellationToken = s.cancellationToken;
        var serviceProvider = s.serviceProvider;
        var endpoint = s.endpoint;
        var routine = s.routine;
        var formatter = s.formatter;
        var shouldLog = s.shouldLog;
        NpgsqlConnection? connection = null;
        StringBuilder? cmdLog = null;
        StringBuilder? cacheKeys = null;
        StringBuilder? commandTextBuilder = null;

        if (Options.AmbientConnectionAccessor?.Invoke() is { } ambientConnection)
        {
            // Ambient connection override (SQL test runner): run on the caller's already-open
            // connection/transaction and never dispose it. The caller owns the connection lifecycle.
            s.connection = connection = ambientConnection;
            s.shouldDispose = false;
        }
        else if (endpoint.ConnectionName is not null)
        {
            // First check if there's a data source for this connection name (for multi-host support)
            if (Options.DataSources?.TryGetValue(endpoint.ConnectionName, out var namedDataSource) is true)
            {
                s.connection = connection = namedDataSource.CreateConnection();
            }
            else if (Options.ConnectionStrings?.TryGetValue(endpoint.ConnectionName, out var connectionString) is true)
            {
                s.connection = connection = new(connectionString);
            }
            else
            {
                await ReturnErrorAsync($"Connection name {endpoint.ConnectionName} could not be found in options DataSources or ConnectionStrings dictionaries.", true, context, cancellationToken);
                return false;
            }
        }
        else if (Options.ServiceProviderMode != ServiceProviderObject.None)
        {
            serviceProvider ??= context.RequestServices;
            if (serviceProvider is null)
            {
                await ReturnErrorAsync($"ServiceProvider must be provided when ServiceProviderMode is set to {Options.ServiceProviderMode}.", true, context, cancellationToken);
                return false;
            }
            if (Options.ServiceProviderMode == ServiceProviderObject.NpgsqlDataSource)
            {
                s.connection = connection = serviceProvider.GetRequiredService<NpgsqlDataSource>().CreateConnection();
                await connection.OpenRetryAsync(Options.ConnectionRetryOptions, cancellationToken);
            }
            else if (Options.ServiceProviderMode == ServiceProviderObject.NpgsqlConnection)
            {
                s.shouldDispose = false;
                s.connection = connection = serviceProvider.GetRequiredService<NpgsqlConnection>();
            }
        }
        else
        {
            if (Options.DataSource is not null)
            {
                s.connection = connection = Options.DataSource.CreateConnection();
            }
            else
            {
                s.connection = connection = new(Options.ConnectionString);
            }
        }

        if (connection is null)
        {
            await ReturnErrorAsync("Connection did not initialize!", log: true, context, cancellationToken);
            return false;
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
            s.noticeHandler = CreateNoticeHandler(context, endpoint, noticeWarnEnabled);
            connection.Notice += s.noticeHandler;
        }

        // Basic Auth for the primary endpoint (see _basicAuthUpfront); remembered so it is not repeated below.
        if (Options.AuthenticationOptions.BasicAuth.Enabled is true || _basicAuthUpfront)
        {
            s.basicAuthCheckedFor = endpoint;
            if (Options.AuthenticationOptions.BasicAuth.ChallengeCommand is not null || endpoint.BasicAuth?.ChallengeCommand is not null)
            {
                await OpenConnectionAsync(connection, context, endpoint, cancellationToken);
            }
            await BasicAuthHandler.HandleAsync(context, endpoint, connection, cancellationToken);
            if (context.Response.HasStarted is true)
            {
                return false;
            }
        }

        // Upfront authorization (see _rejectAnonymousUpfront): reject before any request processing work.
        if ((_rejectAnonymousUpfront || _checkRolesUpfront)
            && await RejectUnauthorizedAsync(context, endpoint, checkAuthentication: _rejectAnonymousUpfront, checkRoles: _checkRolesUpfront))
        {
            return false;
        }

        // Disposed with the request (see CompleteRequestAsync).
        var command = s.command = NpgsqlRestCommand.Create(connection);

        if (shouldLog)
        {
            cmdLog = s.cmdLog = StringBuilderPool.Rent();
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
            commandTextBuilder = s.commandTextBuilder = StringBuilderPool.Rent(routine.Expression.Length + routine.ParamCount * 16);
            commandTextBuilder.Append(routine.Expression);
        }

        // paramsList
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
        s.uploadHandler = (endpoint.Upload is true && !skipUploadForProxy) ? endpoint.CreateUploadHandler() : null;
        s.customHttpTypes = Options.HttpClientOptions.Enabled ? new(routine.Parameters.Length) : null!;

        if (endpoint.Cached is true)
        {
            var prefixLen = endpoint.CacheKeyPrefix?.Length ?? 0;
            cacheKeys = s.cacheKeys = StringBuilderPool.Rent(routine.Expression.Length + prefixLen + (endpoint.CachedParams?.Count ?? 0) * 32);
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

        // Claim-bound user parameters (user_params) are filled by the server: for signed-in callers the claim
        // always wins (see the parameter loops). Anonymous callers must not be able to supply them either -
        // otherwise ?userId=1 would impersonate user 1 on an endpoint that allows anonymous access. Their
        // request values are dropped, so the routine sees "not provided" (its default, else NULL).
        if (endpoint.UseUserParameters && context.User?.Identity?.IsAuthenticated is not true)
        {
            RemoveClientSuppliedUserParameters(routine, ref queryCollection, bodyDict);
        }

        // Parameter binding (query string or JSON body). It may resolve an overload: s.routine, s.endpoint and
        // s.formatter are then the overload's.
        s.queryCollection = queryCollection;
        s.bodyDict = bodyDict;
        s.body = body;
        s.jsonObj = jsonObj;
        if (endpoint.RequestParamType == RequestParamType.QueryString)
        {
            return await BindQueryStringParametersAsync(s);
        }
        if (endpoint.RequestParamType == RequestParamType.BodyJson)
        {
            return await BindBodyParametersAsync(s);
        }
        return true;
    }

    /// <summary>
    /// The per-request connection notice handler. A separate method so its closure is allocated only for the requests
    /// that attach a handler (it captures the request and the endpoint, never the pooled request state).
    /// </summary>
    private static NoticeEventHandler CreateNoticeHandler(HttpContext context, RoutineEndpoint currentEndpointCaptured, bool noticeWarnEnabled)
    {
        return (sender, args) =>
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

    /// <summary>
    /// Second stage: the parameters are bound. Validation, encryption, authorization of the resolved routine, the
    /// command text, resolved parameter expressions, placeholder values, response headers, HTTP Custom Types and the
    /// cache key. Returns false when the request is finished.
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> PrepareParametersAsync(RequestState s)
    {
        var context = s.context;
        var cancellationToken = s.cancellationToken;
        var endpoint = s.endpoint;
        var routine = s.routine;
        var formatter = s.formatter;
        var connection = s.connection!;
        var command = s.command;
        var commandTextBuilder = s.commandTextBuilder;
        var cacheKeys = s.cacheKeys;
        var body = s.body;
        var customHttpTypes = s.customHttpTypes;
        string? commandText = null;

        if (s.hasNulls && routine.IsStrict)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            await context.Response.CompleteAsync();
            return false;
        }

        // parameters are ready !!!

        // Validate parameters before any further processing (authorization, proxy, etc.)
        if (endpoint.ParameterValidations is not null)
        {
            if (!await ValidateParametersAsync(command.Parameters, endpoint, context, cancellationToken))
            {
                return false;
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

        // Basic Auth for the routine the request resolved to, when it was not done upfront for it: an overload with
        // its own basic_auth (it used to run without credentials when the primary had none), or a path whose
        // overloads do not all use Basic Auth (the public ones used to be challenged by the primary's).
        if (ReferenceEquals(s.basicAuthCheckedFor, endpoint) is false && endpoint.BasicAuth?.Enabled is true)
        {
            if (endpoint.BasicAuth.ChallengeCommand is not null || Options.AuthenticationOptions.BasicAuth.ChallengeCommand is not null)
            {
                await OpenConnectionAsync(connection, context, endpoint, cancellationToken);
            }
            await BasicAuthHandler.HandleAsync(context, endpoint, connection, cancellationToken);
            if (context.Response.HasStarted is true)
            {
                return false;
            }
        }

        // authorization check for the endpoint the request resolved to (it may be an overload)
        if (endpoint.Login is false
            && await RejectUnauthorizedAsync(context, endpoint, checkAuthentication: true, checkRoles: true))
        {
            return false;
        }

        if (formatter.IsFormattable is true)
        {
            if (formatter.RefContext)
            {
                commandText = formatter.FormatCommand(routine, command.Parameters, context);
                if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
                {
                    return false;
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
                        return false;
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

        s.commandText = commandText;
        if (commandText is null)
        {
            s.shouldCommit = false;
            s.uploadHandler?.OnError(connection, context, null);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.CompleteAsync();
            return false;
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
        // HTTP Custom Type URLs get their own lookup: values that come from the request are percent-encoded there
        // (env var values - operator configuration such as a base URL - are not), so a request value cannot
        // change the outbound request's path, query or host.
        Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>>? urlLookup = null;
        if (endpoint.HeadersNeedParsing is true || endpoint.CustomParamsNeedParsing || HttpClientTypes.NeedsParsing)
        {
            // Case-insensitive so `{name}` placeholders match parameter names regardless of casing,
            // consistent with the resolved-parameter SQL expression resolver (Formatter.ParameterizeSqlExpression).
            var envVars = Options.SubstitutionEnvironmentVariables;
            Dictionary<string, string> replacements = new(command.Parameters.Count * 4 + (envVars?.Count ?? 0), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string>? urlReplacements = HttpClientTypes.NeedsParsing
                ? new(command.Parameters.Count * 4 + (envVars?.Count ?? 0), StringComparer.OrdinalIgnoreCase)
                : null;
            // Allowlisted env vars first (base); parameter values added below overwrite them on a name
            // collision, so a routine parameter always wins over an env var of the same name.
            // Entries under a "!NAME" key mark names that resolved to a real value - that is what the
            // {!NAME}/{!NAME:fallback} forms key off in Formatter.TryResolveToken.
            if (envVars is not null)
            {
                foreach (var (name, value) in envVars)
                {
                    replacements[name] = value;
                    if (urlReplacements is not null)
                    {
                        urlReplacements[name] = value;
                    }
                }
            }
            for (var i = 0; i < command.Parameters.Count; i++)
            {
                var isNull = command.Parameters[i].Value is null || command.Parameters[i].Value == DBNull.Value;
                var value = isNull ? "" : command.Parameters[i].Value?.ToString() ?? "";
                var param = (NpgsqlRestParameter)command.Parameters[i];
                // A parameter wins over an env var in BOTH forms: a non-null value registers its own
                // "!name" key; a null value removes any "!name" an env var of the same name left behind,
                // so {!name:fallback} then yields the fallback instead of the env value.
                var urlValue = urlReplacements is null ? null : Uri.EscapeDataString(value);
                void AddParam(string key)
                {
                    replacements[key] = value;
                    if (isNull is false)
                    {
                        replacements[string.Concat("!", key)] = value;
                    }
                    else if (envVars is not null)
                    {
                        replacements.Remove(string.Concat("!", key));
                    }
                    if (urlReplacements is not null)
                    {
                        urlReplacements[key] = urlValue!;
                        if (isNull is false)
                        {
                            urlReplacements[string.Concat("!", key)] = urlValue!;
                        }
                        else if (envVars is not null)
                        {
                            urlReplacements.Remove(string.Concat("!", key));
                        }
                    }
                }
                if (!string.IsNullOrEmpty(param.ActualName))
                {
                    AddParam(param.ActualName);
                }
                AddParam(param.ConvertedName);
                if (param.TypeDescriptor.CustomTypeName is not null)
                {
                    AddParam(param.TypeDescriptor.CustomTypeName);
                }
            }
            lookup = replacements.GetAlternateLookup<ReadOnlySpan<char>>();
            urlLookup = urlReplacements?.GetAlternateLookup<ReadOnlySpan<char>>();
        }

        if (endpoint.HeadersNeedParsing is true || endpoint.CustomParamsNeedParsing)
        {
            if (endpoint.CustomParamsNeedParsing && endpoint.CustomParameters is not null)
            {
                customParameters = new Dictionary<string, string>(endpoint.CustomParameters.Count);
                foreach (var (key, value) in endpoint.CustomParameters)
                {
                    // SQL-valued parameters (row_command) are never formatted: request values in SQL text = SQL injection.
                    customParameters[key] = Defaults.DefaultCommentParser.IsSqlCustomParameter(key)
                        ? value
                        : Formatter.FormatString(value, lookup!.Value).ToString();
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
            await HttpClientTypeHandler.InvokeAllAsync(customHttpTypes, lookup, command.Parameters, cancellationToken, urlLookup);
        }

        // A proxied response depends on the whole forwarded request, not only on the routine parameters:
        // query string keys that are not routine parameters are forwarded too, and so is the request body.
        if (cacheKeys is not null && endpoint.IsProxy)
        {
            var rawQuery = context.Request.QueryString.Value ?? "";
            cacheKeys.Append(NpgsqlRestParameter.GetCacheKeySeparator()).Append("?=").Append(rawQuery.Length).Append(':').Append(rawQuery);
            if (body is not null)
            {
                cacheKeys.Append(NpgsqlRestParameter.GetCacheKeySeparator()).Append("body=").Append(body.Length).Append(':').Append(body);
            }
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

        s.customParameters = customParameters;
        s.cacheKeyString = cacheKeyString;
        s.resolvedCache = resolvedCache;
        s.bypassCache = bypassCache;
        s.cacheTtlOverride = cacheTtlOverride;
        return true;
    }

    /// <summary>
    /// Reverse proxy endpoints: forwards the request upstream. Returns true when the routine still has to run (the
    /// upstream response was mapped to its parameters), false when the proxied response was written.
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> ProxyAsync(RequestState s)
    {
        // Locals, not the pooled request state: the cache factory below captures them.
        var context = s.context;
        var cancellationToken = s.cancellationToken;
        var endpoint = s.endpoint;
        var command = s.command;
        var body = s.body;
        var shouldLog = s.shouldLog;
        var cmdLog = s.cmdLog;
        var commandText = s.commandText!;
        var resolvedCache = s.resolvedCache;
        var cacheKeyString = s.cacheKeyString;
        var bypassCache = s.bypassCache;
        var cacheTtlOverride = s.cacheTtlOverride;
        var claimsDict = s.claimsDict;

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

            // Passthrough proxy endpoints (those without proxy response parameters) may be cached;
            // the cache read/execute/coalesce is handled at the InvokeAsync call below.
            bool isPassthroughProxy = !endpoint.HasProxyResponseParameters;

            // Build user context headers if UserContext is enabled
            Dictionary<string, string>? userContextHeaders = null;
            if (endpoint.UserContext is true)
            {
                userContextHeaders = [];
                claimsDict = s.claimsDict ??= context.User.BuildClaimsDictionary(Options.AuthenticationOptions);

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

            // Cacheable passthrough proxy: route the upstream call through GetOrCreateAsync so a burst
            // of identical requests coalesces into a single upstream call (stampede protection), and
            // the response is cached for subsequent hits.
            bool proxyCacheable = isPassthroughProxy && endpoint.Cached is true && bypassCache is false && resolvedCache is not null;
            if (proxyCacheable)
            {
                bool factoryRan = false;
                var cachedProxy = await resolvedCache!.GetOrCreateAsync(
                    endpoint,
                    cacheKeyString!,
                    async ct =>
                    {
                        factoryRan = true;
                        return await Proxy.ProxyRequestHandler.InvokeAsync(context, endpoint, body, command.Parameters, userContextHeaders, ct);
                    },
                    cacheTtlOverride,
                    cancellationToken);

                if (shouldLog && factoryRan is false)
                {
                    cmdLog?.AppendLine("/* proxy response from cache */");
                    NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", commandText);
                }
                if (cachedProxy is Proxy.ProxyResponse cachedProxyResponse)
                {
                    await Proxy.ProxyRequestHandler.WriteResponseAsync(context, cachedProxyResponse, Options.ProxyOptions, cancellationToken);
                }
                return false;
            }

            var proxyResponse = await Proxy.ProxyRequestHandler.InvokeAsync(context, endpoint, body, command.Parameters, userContextHeaders, cancellationToken);

            if (endpoint.HasProxyResponseParameters)
            {
                // Map proxy response to parameters and continue with routine execution
                MapProxyResponseToParameters(proxyResponse, command.Parameters, endpoint);
            }
            else
            {
                // No proxy parameters and not cacheable - return proxy response directly without PostgreSQL.
                await Proxy.ProxyRequestHandler.WriteResponseAsync(context, proxyResponse, Options.ProxyOptions, cancellationToken);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Sets the request context on the connection before the routine runs: request headers, user claims and IP
    /// address, BeforeRoutineCommands, and the request transaction when WrapInTransaction is on. Does nothing (and
    /// does not open the connection) when none of them applies.
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask SetRequestContextAsync(RequestState s)
    {
        var context = s.context;
        var cancellationToken = s.cancellationToken;
        var endpoint = s.endpoint;
        var connection = s.connection!;
        var headers = s.headers;
        var transaction = s.transaction;
        var claimsDict = s.claimsDict;

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
                transaction = s.transaction = await connection.BeginTransactionAsync(cancellationToken);
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
                    claimsDict = s.claimsDict ??= context.User.BuildClaimsDictionary(Options.AuthenticationOptions);

                    if (Options.AuthenticationOptions.IpAddressContextKey is not null)
                    {
                        var cmd = new NpgsqlBatchCommand(setContextSql);
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.AuthenticationOptions.IpAddressContextKey));
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(context.Request.GetClientIpAddressDbParam()));
                        batch.BatchCommands.Add(cmd);
                    }
                    // Every claim context key is set on every user_context request - for anonymous requests to
                    // NULL. Session-level settings (no transaction) survive on a pooled connection when the
                    // connection is not reset on close (NoResetOnClose), so skipping them for anonymous requests
                    // would let an anonymous request run with the previous request's user identity.
                    var isAuthenticated = context.User?.Identity?.IsAuthenticated is true;
                    if (Options.AuthenticationOptions.ClaimsJsonContextKey is not null)
                    {
                        var cmd = new NpgsqlBatchCommand(setContextSql);
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(Options.AuthenticationOptions.ClaimsJsonContextKey));
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(isAuthenticated ? claimsDict?.GetUserClaimsDbParam() ?? DBNull.Value : DBNull.Value));
                        batch.BatchCommands.Add(cmd);
                    }
                    foreach (var mapping in Options.AuthenticationOptions.ContextKeyClaimsMapping)
                    {
                        var cmd = new NpgsqlBatchCommand(setContextSql);
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(mapping.Key));
                        cmd.Parameters.Add(NpgsqlRestParameter.CreateTextParam(isAuthenticated ? claimsDict!.GetClaimDbContextParam(mapping.Value) : DBNull.Value));
                        batch.BatchCommands.Add(cmd);
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
                                    claimsDict = s.claimsDict ??= context.User.BuildClaimsDictionary(Options.AuthenticationOptions);
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
    }

    /// <summary>Upload endpoints: runs the upload handler and passes its metadata to the routine.</summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask UploadAsync(RequestState s)
    {
        var context = s.context;
        var cancellationToken = s.cancellationToken;
        var endpoint = s.endpoint;
        var connection = s.connection!;
        var command = s.command;
        var uploadHandler = s.uploadHandler;
        var transaction = s.transaction;
        var customParameters = s.customParameters;
        var uploadMetaParamIndex = s.uploadMetaParamIndex;

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
                transaction = s.transaction = await connection.BeginTransactionAsync(cancellationToken);
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
    }

    /// <summary>
    /// Login, logout and cache invalidation endpoints. Returns true when the routine's result still has to be
    /// rendered (a login with SerializeAuthEndpointsResponse), false when the request is finished.
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> LoginLogoutOrInvalidateAsync(RequestState s)
    {
        var context = s.context;
        var cancellationToken = s.cancellationToken;
        var endpoint = s.endpoint;
        var connection = s.connection!;
        var command = s.command;
        var commandText = s.commandText!;
        var shouldLog = s.shouldLog;
        var cmdLog = s.cmdLog;
        var resolvedCache = s.resolvedCache;
        var cacheKeys = s.cacheKeys;

        if (endpoint.Login is true)
        {
            if (await PrepareCommand(connection, command, commandText, context, endpoint, false, cancellationToken) is false)
            {
                return false;
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
                return false;
            }
        }

        if (endpoint.Logout is true)
        {
            if (await PrepareCommand(connection, command, commandText, context, endpoint, true, cancellationToken) is false)
            {
                return false;
            }
            if (shouldLog)
            {
                NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", command.CommandText);
            }
            await LogoutHandler.HandleAsync(command, endpoint, context, cancellationToken);
            return false;
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
            return false;
        }

        return true;
    }

    /// <summary>
    /// A request failed: writes the error response (when nothing was sent yet), notifies the upload handler and logs.
    /// </summary>
    private async Task HandleExceptionAsync(RequestState s, Exception exception)
    {
        // The command was disposed before the error was handled when it was an `await using` local.
        await DisposeCommandAsync(s);

        var context = s.context;
        var endpoint = s.endpoint;
        var uploadHandler = s.uploadHandler;
        var connection = s.connection;
        var shouldLog = s.shouldLog;
        var cmdLog = s.cmdLog;
        var commandText = s.commandText;

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
        else if (exception is PostgresException postgresEx)
        {
            // An error raised by PostgreSQL (RAISE EXCEPTION, constraint violations, ...): its message is part of
            // the routine's contract with the client.
            if (context.Response.HasStarted is false)
            {
                await Results.Problem(
                    type: null,
                    statusCode: 500,
                    title: postgresEx.Message,
                    detail: postgresEx.SqlState).ExecuteAsync(context);
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
        }
        else if (exception is NpgsqlException)
        {
            // A client-side Npgsql failure (connection, timeout, I/O): its message describes the infrastructure
            // (hosts, ports, pool state) and is logged below, not returned.
            if (context.Response.HasStarted is false)
            {
                await Results.Problem(
                    type: null,
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: null).ExecuteAsync(context);
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
                // The exception message is logged below, not returned: it describes server internals.
                await Results.Problem(
                    type: null, // remove RFC URL
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: null).ExecuteAsync(context);
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

    /// <summary>
    /// The end of every request: completes the response (and proxy_out), commits the transaction, detaches the notice
    /// handler, disposes the connection and returns the pooled builders.
    /// </summary>
    private async Task CompleteRequestAsync(RequestState s)
    {
        await DisposeCommandAsync(s);

        var writer = s.writer;
        var proxyOutBuffer = s.proxyOutBuffer;
        var proxyOutOriginalBody = s.proxyOutOriginalBody;
        var context = s.context;
        var endpoint = s.endpoint;
        var cancellationToken = s.cancellationToken;
        var transaction = s.transaction;
        var connection = s.connection;
        var shouldCommit = s.shouldCommit;
        var noticeHandler = s.noticeHandler;
        var shouldDispose = s.shouldDispose;
        var cmdLog = s.cmdLog;
        var cacheKeys = s.cacheKeys;
        var commandTextBuilder = s.commandTextBuilder;

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
        // Detach the per-request notice handler so it can't accumulate (and re-log) on a reused
        // connection that this request did not dispose.
        if (connection is not null && noticeHandler is not null)
        {
            connection.Notice -= noticeHandler;
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
    }

    private static ValueTask DisposeCommandAsync(RequestState s)
    {
        var command = s.command;
        if (command is null)
        {
            return default;
        }
        s.command = null!;
        return command.DisposeAsync();
    }

    /// <summary>
    /// Multi-command routines (SQL files with several statements): executes each statement and writes one JSON object
    /// with a property per statement.
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask RenderMultiCommandAsync(RequestState s)
    {
        var context = s.context;
        var endpoint = s.endpoint;
        var routine = s.routine;
        var connection = s.connection!;
        var command = s.command;
        var writer = s.writer;
        var cmdLog = s.cmdLog;
        var shouldLog = s.shouldLog;
        var cancellationToken = s.cancellationToken;

        // The request's row renderer; non-null only while a statement's rows are being rendered.
        RowRenderer? mcRenderer = null;
        try
        {
            var multiCommandInfo = routine.MultiCommandInfo!; // checked by the caller
            await OpenConnectionAsync(connection, context, endpoint, cancellationToken);

            if (shouldLog)
            {
                if (routine.LogCommandText)
                {
                    var allSql = string.Join(";\n", multiCommandInfo.Select(c => c.Statement));
                    NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", allSql);
                }
                else
                {
                    NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "",
                        string.Concat(routine.SimpleDefinition, " (", multiCommandInfo.Length.ToString(), " statements)"));
                }
            }

            // Void multi-command: execute all statements, return 204
            if (endpoint.Void)
            {
                for (int cmdIndex = 0; cmdIndex < multiCommandInfo.Length; cmdIndex++)
                {
                    var currentCmd = multiCommandInfo[cmdIndex];
                    await using var mcCmd = new NpgsqlCommand(currentCmd.Statement, connection);
                    if (endpoint.CommandTimeout.HasValue)
                    {
                        mcCmd.CommandTimeout = ToCommandTimeoutSeconds(endpoint.CommandTimeout.Value);
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
            for (int cmdIndex = 0; cmdIndex < multiCommandInfo.Length; cmdIndex++)
            {
                var currentCmd = multiCommandInfo[cmdIndex];

                // Build a command for this statement
                await using var mcCmd = new NpgsqlCommand(currentCmd.Statement, connection);
                if (endpoint.CommandTimeout.HasValue)
                {
                    mcCmd.CommandTimeout = ToCommandTimeoutSeconds(endpoint.CommandTimeout.Value);
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
                    var mcBufferRows = endpoint.BufferRows ?? Options.BufferRows;
                    var mcResponseBufferBytes = Options.ResponseBufferBytes;
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
                    // Rows are rendered as UTF-8 by the same renderer as single-statement results.
                    mcRenderer = s.rowRenderer ??= new RowRenderer();
                    mcRenderer.Init(mcReader, routine, endpoint, connection);
                    mcRenderer.Configure(currentCmd.ColumnNames, currentCmd.JsonColumnNames, currentCmd.ColumnTypeDescriptors,
                        currentCmd.ColumnCount, mcNestedMap, hiddenColumnIndex: -1, currentCmd.ReturnsUnnamedSet, raw: false);
                    var mcRow = mcRenderer.Row;

                    while (await mcReader.ReadAsync(cancellationToken))
                    {
                        mcRowCount++;
                        if (!mcFirstRow)
                        {
                            mcRow.Append(Consts.Comma);
                        }
                        mcFirstRow = false;

                        mcRenderer.AppendRow();

                        if (currentCmd.IsSingle)
                        {
                            break;
                        }

                        // The same rule as for single-statement results: 0 = send every row, 1 = buffer all rows,
                        // N = every N rows, send what is collected once it is at least ResponseBufferBytes.
                        // (Rows that were sent used to stay in the buffer here, so a statement returning more than
                        // BufferRows rows repeated them in the response.)
                        if (mcBufferRows == 0 ||
                            (mcBufferRows != 1 && mcRowCount % mcBufferRows == 0 && mcRow.Length >= mcResponseBufferBytes))
                        {
                            writer.Write(mcRow.WrittenSpan);
                            await writer.FlushAsync(cancellationToken);
                            mcRow.Clear();
                        }
                    }

                    if (currentCmd.IsSingle && mcRowCount == 0)
                    {
                        // Empty result with @single — write null
                        writer.Write(Consts.Utf8Null);
                    }

                    if (mcRow.Length > 0)
                    {
                        writer.Write(mcRow.WrittenSpan);
                    }
                    mcRenderer.Release();
                    mcRenderer = null;

                    if (!currentCmd.IsSingle)
                    {
                        writer.Write(Consts.Utf8CloseBracket);
                    }
                }
            }

            // Write closing }
            writer.Write(Consts.Utf8CloseBrace);
            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            mcRenderer?.Release();
        }
    }

    /// <summary>Void routines: executes the command and answers 204 No Content.</summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask RenderVoidAsync(RequestState s)
    {
        if (await PrepareCommand(s.connection!, s.command, s.commandText!, s.context, s.endpoint, true, s.cancellationToken) is false)
        {
            return;
        }
        if (s.shouldLog)
        {
            NpgsqlRestLogger.LogEndpoint(s.endpoint, s.cmdLog?.ToString() ?? "", s.command.CommandText);
        }
        // The first attempt is made here rather than in the retrying method, so that one async method less waits on
        // the database with every request. If it fails, the retrying method takes over and handles the failure
        // exactly as if it had made the attempt itself (error mapping, retries, logging).
        try
        {
            await s.command.ExecuteNonQueryAsync(s.cancellationToken);
        }
        catch (Exception firstFailure) when (Retries(s.endpoint))
        {
            await s.command.ExecuteNonQueryAfterFailureAsync(
                firstFailure,
                s.endpoint.RetryStrategy!,
                s.cancellationToken,
                s.endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
        }
        s.context.Response.StatusCode = (int)HttpStatusCode.NoContent;
    }

    private static bool Retries(RoutineEndpoint endpoint) => endpoint.RetryStrategy is { RetrySequenceSeconds.Length: > 0 };

    // Scalar values larger than this are sent in pieces of this size (each flushed), so a large value is not copied
    // into the response buffers all at once.
    private const int ScalarChunkBytes = 64 * 1024;

    /// <summary>
    /// A routine returning a single value (not cached). This method waits for the database and keeps nothing but the
    /// request state alive while it does; <see cref="WriteScalarAsync"/> then reads and writes the value.
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask RenderScalarAsync(RequestState s)
    {
        if (await PrepareCommand(s.connection!, s.command, s.commandText!, s.context, s.endpoint, true, s.cancellationToken) is false)
        {
            return;
        }

        // The first attempt is made here (see RenderVoidAsync).
        if (Retries(s.endpoint))
        {
            s.cancellationToken.ThrowIfCancellationRequested();
        }
        NpgsqlDataReader reader;
        try
        {
            reader = await s.command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, s.cancellationToken);
        }
        catch (Exception firstFailure) when (Retries(s.endpoint))
        {
            reader = await s.command.ExecuteReaderAfterFailureAsync(
                firstFailure,
                CommandBehavior.SequentialAccess,
                s.endpoint.RetryStrategy!,
                s.cancellationToken,
                s.endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
        }
        await WriteScalarAsync(s, reader);
    }

    /// <summary>
    /// Reads the single value from the reader, closes the reader, and writes the value. Text values - notably json -
    /// go from the reader to the response as UTF-8 bytes and never become a string: a 1 MB json document used to be a
    /// 2 MB UTF-16 string, encoded back through a 3 MB buffer. The value is read completely and the reader closed
    /// before anything is written, so an error that PostgreSQL reports after the row (a deferred constraint, for
    /// example) still produces an error response.
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask WriteScalarAsync(RequestState s, NpgsqlDataReader reader)
    {
        TypeDescriptor descriptor;
        bool asUtf8;
        byte[]? utf8 = null; // rented
        int utf8Length = 0;
        object? valueResult = null;
        try
        {
            await using (reader)
            {
                if (s.shouldLog)
                {
                    NpgsqlRestLogger.LogEndpoint(s.endpoint, s.cmdLog?.ToString() ?? "", s.command.CommandText);
                }
                if (await reader.ReadAsync(s.cancellationToken) is false)
                {
                    Logger?.CouldNotReadCommand(s.command.CommandText, s.context.Request.Method, s.context.Request.Path);
                    s.context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return;
                }
                descriptor = s.routine.ColumnsTypeDescriptor[0];
                // UTF-8 bytes straight from the reader, unless the value is binary or must be decrypted (that needs
                // the string).
                asUtf8 = descriptor.IsBinary is false &&
                    (Options.AuthenticationOptions.DefaultDataProtector is null ||
                     (s.endpoint.DecryptAllColumns is false && s.endpoint.DecryptColumns is null)) &&
                    IsUtf8Wire(s.connection!);
                if (asUtf8 is false)
                {
                    valueResult = descriptor.IsBinary ? reader.GetFieldValue<byte[]>(0) : reader.GetValue(0) as string;
                }
                else if (reader.IsDBNull(0) is false)
                {
                    await using var stream = reader.GetStream(0);
                    int length = checked((int)stream.Length);
                    utf8 = ArrayPool<byte>.Shared.Rent(length);
                    while (utf8Length < length)
                    {
                        int read = await stream.ReadAsync(utf8.AsMemory(utf8Length, length - utf8Length), s.cancellationToken);
                        if (read == 0)
                        {
                            break;
                        }
                        utf8Length += read;
                    }
                }
            }

            if (asUtf8 is false)
            {
                await WriteScalarValueAsync(s, descriptor, valueResult);
                return;
            }

            SetScalarContentType(s.context, descriptor);
            if (utf8 is null)
            {
                // SQL NULL
                if (s.endpoint.Raw is false)
                {
                    if (s.endpoint.TextResponseNullHandling == TextResponseNullHandling.NullLiteral)
                    {
                        s.writer.Write(Consts.Utf8Null);
                    }
                    else if (s.endpoint.TextResponseNullHandling == TextResponseNullHandling.NoContent)
                    {
                        s.context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                    }
                    // else OK empty string
                }
                return;
            }
            if (descriptor.IsArray && s.endpoint.Raw is false)
            {
                var json = Utf8Buffer.Rent();
                try
                {
                    PgUtf8Converters.AppendPgArrayAsJson(json, utf8.AsSpan(0, utf8Length), descriptor);
                    s.writer.Write(json.WrittenSpan);
                }
                finally
                {
                    Utf8Buffer.Return(json);
                }
            }
            else if (utf8Length <= ScalarChunkBytes)
            {
                s.writer.Write(utf8.AsSpan(0, utf8Length));
            }
            else
            {
                for (int offset = 0; offset < utf8Length; offset += ScalarChunkBytes)
                {
                    s.writer.Write(utf8.AsSpan(offset, Math.Min(ScalarChunkBytes, utf8Length - offset)));
                    await s.writer.FlushAsync(s.cancellationToken);
                }
            }
        }
        finally
        {
            if (utf8 is not null)
            {
                ArrayPool<byte>.Shared.Return(utf8);
            }
        }
    }

    /// <summary>
    /// A routine returning a single value, with response caching: a burst of identical requests runs the routine once.
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask RenderCachedScalarAsync(RequestState s)
    {
        // Locals, not the pooled request state: the cache factory below captures them.
        var context = s.context;
        var endpoint = s.endpoint;
        var connection = s.connection!;
        var command = s.command;
        var commandText = s.commandText!;
        var cmdLog = s.cmdLog;
        var shouldLog = s.shouldLog;
        var resolvedCache = s.resolvedCache!;
        var cacheKeyString = s.cacheKeyString;
        var cacheTtlOverride = s.cacheTtlOverride;
        var cancellationToken = s.cancellationToken;
        TypeDescriptor descriptor = s.routine.ColumnsTypeDescriptor[0];

        object? valueResult;
        // Route through GetOrCreateAsync so a burst of identical requests coalesces into a
        // single connection-open + execution (stampede protection). The connection is opened
        // by PrepareCommand *inside* the factory, so coalesced waiters never touch the DB.
        bool factoryRan = false;
        try
        {
            valueResult = await resolvedCache.GetOrCreateAsync(
                endpoint,
                cacheKeyString!,
                async ct =>
                {
                    factoryRan = true;
                    if (await PrepareCommand(connection, command, commandText, context, endpoint, true, ct) is false)
                    {
                        throw new CacheFactoryAbortException(CacheFactoryAbortReason.ResponseAlreadyWritten);
                    }

                    await using var reader = await command.ExecuteReaderWithRetryAsync(
                        CommandBehavior.SequentialAccess,
                        endpoint.RetryStrategy,
                        ct,
                        errorCodePolicy: endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                    if (shouldLog)
                    {
                        NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", command.CommandText);
                    }
                    if (await reader.ReadAsync(ct))
                    {
                        return descriptor.IsBinary ? reader.GetFieldValue<byte[]>(0) : reader.GetValue(0) as string;
                    }
                    throw new CacheFactoryAbortException(CacheFactoryAbortReason.NoRow);
                },
                cacheTtlOverride,
                cancellationToken);
        }
        catch (CacheFactoryAbortException abort)
        {
            if (abort.Reason == CacheFactoryAbortReason.NoRow)
            {
                Logger?.CouldNotReadCommand(command.CommandText, context.Request.Method, context.Request.Path);
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            // ResponseAlreadyWritten: PrepareCommand already wrote the lead caller's response.
            return;
        }
        if (factoryRan is false && shouldLog)
        {
            cmdLog?.AppendLine("/* from cache */");
            NpgsqlRestLogger.LogEndpoint(endpoint, cmdLog?.ToString() ?? "", commandText);
        }

        await WriteScalarValueAsync(s, descriptor, valueResult);
    }

    /// <summary>Writes a scalar value held as a string (or as bytes, for bytea): decrypts it when asked to, then writes it.</summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask WriteScalarValueAsync(RequestState s, TypeDescriptor descriptor, object? valueResult)
    {
        var context = s.context;
        var endpoint = s.endpoint;
        var writer = s.writer;
        var cancellationToken = s.cancellationToken;

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

        SetScalarContentType(context, descriptor);

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
    }

    /// <summary>
    /// A routine returning rows (a record, a set, or a table): JSON, raw/CSV or a table format, with response caching.
    /// This part runs the command; <see cref="WriteResultSetsAsync"/> writes the rows.
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask RenderRecordsAsync(RequestState s)
    {
        // Held (when non-null) for the streaming records/sets cache path; released in the finally.
        RecordCacheGate? recordCacheGate = null;
        try
        {
            var binary = s.routine.ColumnsTypeDescriptor.Length == 1 && s.routine.ColumnsTypeDescriptor[0].IsBinary;

            // Check cache for records/sets (but not for binary or raw mode)
            var canCacheRecordsAndSets = s.resolvedCache is not null
                && s.endpoint.Cached is true
                && s.bypassCache is false
                && binary is false
                && s.endpoint.Raw is false;

            if (canCacheRecordsAndSets)
            {
                // Stampede protection for the streaming set path: serialize concurrent requests
                // for this key. The lead executes and (within MaxCacheableRows) populates the cache
                // below, so the waiters that acquire the gate next fall straight into this cache-hit
                // branch instead of re-executing. Released in the outer finally.
                recordCacheGate = await RecordCacheGate.AcquireAsync(s.resolvedCache!, s.cacheKeyString!, s.cancellationToken);

                if (s.resolvedCache!.Get(s.endpoint, s.cacheKeyString!, out var cachedResult))
                {
                    if (s.shouldLog)
                    {
                        s.cmdLog?.AppendLine("/* from cache */");
                        NpgsqlRestLogger.LogEndpoint(s.endpoint, s.cmdLog?.ToString() ?? "", s.commandText ?? "");
                    }

                    if (s.context.Response.ContentType is null)
                    {
                        s.context.Response.ContentType = Application.Json;
                    }

                    var cachedSpan = (cachedResult as string ?? "").AsSpan();
                    s.writer.Advance(Encoding.UTF8.GetBytes(cachedSpan, s.writer.GetSpan(Encoding.UTF8.GetMaxByteCount(cachedSpan.Length))));
                    return;
                }
            }

            NpgsqlDataReader reader;
            await using NpgsqlBatch? batch = s.routine.IsMultiCommand && s.routine.MultiCommandInfo is not null ? new NpgsqlBatch(s.connection) : null;

            if (batch is not null)
            {
                // Multi-command: use NpgsqlBatch with one command per statement
                foreach (var cmdInfo in s.routine.MultiCommandInfo!)
                {
                    var batchCmd = new NpgsqlBatchCommand(cmdInfo.Statement);
                    // Copy bound parameters from the main command to each batch command
                    for (int pi = 0; pi < Math.Min(s.command.Parameters.Count, cmdInfo.ParamCount); pi++)
                    {
                        var srcParam = s.command.Parameters[pi];
                        var batchParam = NpgsqlRestParameter.CreateParamWithType(srcParam.NpgsqlDbType);
                        batchParam.Value = srcParam.Value ?? DBNull.Value;
                        batchCmd.Parameters.Add(batchParam);
                    }
                    batch.BatchCommands.Add(batchCmd);
                }
                reader = await batch.ExecuteBatchReaderWithRetryAsync(
                    CommandBehavior.SequentialAccess,
                    s.endpoint.RetryStrategy,
                    s.cancellationToken,
                    errorCodePolicy: s.endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                if (s.shouldLog)
                {
                    NpgsqlRestLogger.LogEndpoint(s.endpoint, s.cmdLog?.ToString() ?? "", string.Join("; ", s.routine.MultiCommandInfo.Select(c => c.Statement)));
                }
            }
            else
            {
                // Single command: use NpgsqlCommand as before
                if (await PrepareCommand(s.connection!, s.command, s.commandText!, s.context, s.endpoint, true, s.cancellationToken) is false)
                {
                    return;
                }
                // The first attempt is made here (see RenderVoidAsync).
                if (Retries(s.endpoint))
                {
                    s.cancellationToken.ThrowIfCancellationRequested();
                }
                try
                {
                    reader = await s.command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, s.cancellationToken);
                }
                catch (Exception firstFailure) when (Retries(s.endpoint))
                {
                    reader = await s.command.ExecuteReaderAfterFailureAsync(
                        firstFailure,
                        CommandBehavior.SequentialAccess,
                        s.endpoint.RetryStrategy!,
                        s.cancellationToken,
                        s.endpoint.ErrorCodePolicy ?? Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
                }
                if (s.shouldLog)
                {
                    NpgsqlRestLogger.LogEndpoint(s.endpoint, s.cmdLog?.ToString() ?? "", s.command.CommandText);
                }
            }
            await using var _ = reader; // ensure disposal

            // Pluggable table format renderer
            if (Options.TableFormatHandlers is not null
                && s.customParameters is not null
                && s.customParameters.TryGetValue("table_format", out var tableFormatName)
                && Options.TableFormatHandlers.TryGetValue(tableFormatName, out var tableFormatHandler))
            {
                s.context.Response.ContentType = tableFormatHandler.ContentType;
                var tfBufferRows = s.endpoint.BufferRows ?? Options.BufferRows;
                if (s.routine.IsMultiCommand && s.routine.MultiCommandInfo is not null)
                {
                    // Multi-command: call handler per result set
                    var tfCmdIndex = 0;
                    do
                    {
                        await tableFormatHandler.RenderAsync(reader, s.routine, s.endpoint, s.writer, s.context, tfBufferRows, s.customParameters, s.cancellationToken);
                        tfCmdIndex++;
                    } while (tfCmdIndex < s.routine.MultiCommandInfo.Length && await reader.NextResultAsync(s.cancellationToken));
                }
                else
                {
                    await tableFormatHandler.RenderAsync(reader, s.routine, s.endpoint, s.writer, s.context, tfBufferRows, s.customParameters, s.cancellationToken);
                }
                await s.writer.FlushAsync(s.cancellationToken);
                return;
            }

            await WriteResultSetsAsync(s, reader, binary, canCacheRecordsAndSets);
        }
        finally
        {
            // Release the records/sets stampede gate (if this request acquired it) so the next waiter
            // for this key can proceed - by now the cache is either populated or the response was over-limit.
            recordCacheGate?.Leave();
        }
    }

    /// <summary>Writes the rows of the reader's result set(s) to the response.</summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask WriteResultSetsAsync(RequestState s, NpgsqlDataReader reader, bool binary, bool canCacheRecordsAndSets)
    {
        var context = s.context;
        var endpoint = s.endpoint;
        var routine = s.routine;
        var connection = s.connection!;
        var writer = s.writer;
        var resolvedCache = s.resolvedCache;
        var cacheKeyString = s.cacheKeyString;
        var cacheTtlOverride = s.cacheTtlOverride;
        var cancellationToken = s.cancellationToken;
        // Renders rows as UTF-8 (the request state's renderer, in use from the first row-returning result set)
        RowRenderer? rowRenderer = null;
        Utf8Buffer? cacheBufferRented = null;
        try
        {
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
            Utf8Buffer? cacheBuffer = canCacheRecordsAndSets ? (cacheBufferRented = Utf8Buffer.Rent()) : null;
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




            // The array brackets go through the same pending buffer as the rows (see rowBuilder below), so
            // nothing is written to the response before the first batch of rows is sent.
            var writeArrayBrackets = routine.ReturnsSet && endpoint.Raw is false && binary is false && isSingleRecord is false;

            bool first = true;
            var routineReturnRecordCount = currentColumnCount;
            var activeColumnNames = currentColumnNames ?? routine.ColumnNames;
            var activeJsonColumnNames = currentJsonColumnNames ?? routine.JsonColumnNames;
            var activeColumnDescriptors = currentColumnDescriptors ?? routine.ColumnsTypeDescriptor;

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

            // A login endpoint's record reaches this path only with SerializeAuthEndpointsResponse on (after a
            // successful sign-in that wrote no response). Its password hash column is written as null: the
            // hash is needed for verification, never by the client.
            var hiddenColumnIndex = -1;
            if (endpoint.Login && Options.AuthenticationOptions.HashColumnName is not null)
            {
                hiddenColumnIndex = Array.FindIndex(activeColumnNames,
                    c => string.Equals(c, Options.AuthenticationOptions.HashColumnName, StringComparison.OrdinalIgnoreCase));
            }

            if (rowRenderer is null)
            {
                rowRenderer = s.rowRenderer ??= new RowRenderer();
                rowRenderer.Init(reader, routine, endpoint, connection);
            }
            rowRenderer.Configure(activeColumnNames, activeJsonColumnNames, activeColumnDescriptors, routineReturnRecordCount, nestedJsonColumnMap, hiddenColumnIndex,
                routine.ReturnsUnnamedSet, endpoint.Raw);
            var rowBuilder = rowRenderer.Row;
            if (writeArrayBrackets)
            {
                rowBuilder.Append(Consts.OpenBracket);
            }

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
                rowBuilder.Append(columns.ToString());
            }

            var bufferRows = endpoint.BufferRows ?? Options.BufferRows;
            var responseBufferBytes = Options.ResponseBufferBytes;
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

                if (binary is true)
                {
                    for (var i = 0; i < routineReturnRecordCount; i++)
                    {
                        await writer.WriteAsync(reader.GetFieldValue<byte[]>(0), cancellationToken);
                    }
                }
                else
                {
                    rowRenderer.AppendRow();
                }

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

                // 0 = send every row; 1 = buffer all rows; N = every N rows, send what is collected if it is at
                // least ResponseBufferBytes. (0 used to divide by zero here and fail the request.)
                // Each send is a socket write and an HTTP chunk, so small batches are not sent one by one; and
                // while nothing is sent yet, a failing query can still produce a proper error response.
                if (bufferRows == 0 ||
                    (bufferRows != 1 && rowCount % bufferRows == 0 && rowBuilder.Length >= responseBufferBytes))
                {
                    // Append to cache buffer before clearing row
                    if (shouldCache)
                    {
                        cacheBuffer!.Append(rowBuilder);
                    }
                    writer.Write(rowBuilder.WrittenSpan);
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
                if (routine.ReturnsSet && endpoint.Raw is false && isSingleRecord is false)
                {
                    rowBuilder.Append(Consts.CloseBracket);
                }
                if (rowBuilder.Length > 0)
                {
                    // Append remaining rows to cache buffer
                    if (shouldCache)
                    {
                        cacheBuffer!.Append(rowBuilder);
                    }
                    // No flush here: completing the response sends everything that is pending in one write.
                    writer.Write(rowBuilder.WrittenSpan);
                    rowBuilder.Clear();
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
        }
        finally
        {
            rowRenderer?.Release();
            if (cacheBufferRented is not null)
            {
                Utf8Buffer.Return(cacheBufferRented);
            }
        }
    }

    /// <summary>
    /// Binds the routine parameters from the query string (and path, claims, headers, uploads, resolved values).
    /// Returns false when the request is finished (a response was written). Reads and updates the request state in
    /// <paramref name="b"/>, and may switch the request to an overload (routine, endpoint, formatter).
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> BindQueryStringParametersAsync(RequestState b)
    {
        // Early-return guard: queryCollection is non-null for the rest of this block (later
        // dereferences use `!` where the compiler's flow analysis loses the narrowing).
        if (b.queryCollection is null)
        {
            b.shouldCommit = false;
            b.uploadHandler?.OnError(b.connection, b.context, null);
            b.context.Response.StatusCode = StatusCodes.Status404NotFound;
            await b.context.Response.CompleteAsync();
            return false;
        }

        // Account for path parameters when counting query string parameters
        var pathParamCount = b.endpoint.PathParameters?.Length ?? 0;
        if (b.queryCollection.Count + pathParamCount != b.routine.ParamCount && overloads.Count > 0)
        {
            if (overloads.TryGetValue(string.Concat(entry.Key, b.queryCollection.Count + pathParamCount), out var overload))
            {
                b.routine = overload.Endpoint.Routine;
                b.endpoint = overload.Endpoint;
                b.formatter = overload.Formatter;
                if (b.formatter.IsFormattable is false)
                {
                    // Reinitialize commandTextBuilder for the new routine
                    b.commandTextBuilder?.Clear();
                    if (b.commandTextBuilder is null)
                    {
                        b.commandTextBuilder = StringBuilderPool.Rent(b.routine.Expression.Length + b.routine.ParamCount * 16);
                    }
                    b.commandTextBuilder.Append(b.routine.Expression);
                }
            }
        }

        for (int i = 0; i < b.routine.Parameters.Length; i++)
        {
            var parameter = b.routine.Parameters[i].NpgsqlRestParameterMemberwiseClone();

            // Resolved parameter: skip HTTP filling, set to DBNull.Value placeholder.
            // The actual value will be resolved via SQL expression after the parameter loop.
            if (b.endpoint.ResolvedParameterExpressions is not null &&
                (b.endpoint.ResolvedParameterExpressions.ContainsKey(parameter.ActualName) ||
                 b.endpoint.ResolvedParameterExpressions.ContainsKey(parameter.ConvertedName)))
            {
                parameter.Value = DBNull.Value;
                if (Options.HttpClientOptions.Enabled)
                {
                    if (parameter.TypeDescriptor.CustomType is not null)
                    {
                        if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                        {
                            b.customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                        }
                    }
                }
                if (!parameter.IsVirtual) b.command.Parameters.Add(parameter);
                b.hasNulls = true;
                if (b.formatter.IsFormattable is false)
                {
                    if (b.formatter.RefContext)
                    {
                        b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex, b.context));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex));
                    }
                }
                b.paramIndex++;
                if (b.shouldLog && Options.LogCommandParameters)
                {
                    b.cmdLog!.Append("-- $").Append(b.paramIndex).Append(' ')
                        .Append(parameter.TypeDescriptor.OriginalType)
                        .Append(" = (resolved)").AppendLine();
                }
                continue;
            }

            if (parameter.HashOf is not null)
            {
                var hashValueQueryCollection = b.queryCollection!.TryGetValue(parameter.HashOf.ConvertedName, out var hashQsValue) ? hashQsValue.ToString() : null;
                if (string.IsNullOrEmpty(hashValueQueryCollection) is true)
                {
                    parameter.Value = DBNull.Value;
                }
                else
                {
                    parameter.Value = Options.AuthenticationOptions.PasswordHasher?.HashPassword(hashValueQueryCollection) as object ?? DBNull.Value;
                }
            }
            if (b.endpoint.UseUserParameters is true)
            {
                if (parameter.IsFromUserClaims is true && b.claimsDict is null)
                {
                    b.claimsDict = b.context.User.BuildClaimsDictionary(Options.AuthenticationOptions);
                }

                if (parameter.IsIpAddress is true)
                {
                    parameter.Value = b.context.Request.GetClientIpAddressDbParam();
                }
                else if (b.context.User?.Identity?.IsAuthenticated is true && Options.AuthenticationOptions.ParameterNameClaimsMapping.TryGetValue(parameter.ActualName, out var claimType))
                {
                    // Claim auto-bind always wins (security routines depend on it). If the
                    // request also supplied a value, it would otherwise be silently dropped —
                    // surface that as a WARN so a parameter naming collision is visible.
                    if (b.queryCollection is not null
                        && (b.queryCollection.ContainsKey(parameter.ConvertedName) || b.queryCollection.ContainsKey(parameter.ActualName)))
                    {
                        Logger?.ClaimMappedParamReceivedRequestValue(
                            b.endpoint.Path, parameter.ActualName, "query", claimType);
                    }
                    parameter.Value = b.claimsDict!.GetClaimDbParam(claimType);
                }
                else if (b.context.User?.Identity?.IsAuthenticated is true && parameter.IsUserClaims is true)
                {
                    parameter.Value = b.claimsDict?.GetUserClaimsDbParam() ?? DBNull.Value;
                }
            }
            if (parameter.IsUploadMetadata is true)
            {
                //uploadMetaParamIndex = i;
                b.uploadMetaParamIndex = b.command.Parameters.Count; // the last one added
                parameter.Value = DBNull.Value;
            }

            // body parameter
            if (parameter.Value is null && b.endpoint.IsBodyParameter(parameter))
            {
                if (b.body is null)
                {
                    parameter.ParamType = ParamType.BodyParam;
                    parameter.Value = DBNull.Value;
                    b.hasNulls = true;

                    if (Options.ValidateParameters is not null)
                    {
                        Options.ValidateParameters(new ParameterValidationValues(
                            b.context,
                            b.routine,
                            parameter));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    if (Options.ValidateParametersAsync is not null)
                    {
                        await Options.ValidateParametersAsync(new ParameterValidationValues(
                            b.context,
                            b.routine,
                            parameter));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    if (b.endpoint.Cached is true && b.endpoint.CachedParams is not null)
                    {
                        if (b.endpoint.CachedParams.Contains(parameter.ConvertedName) || b.endpoint.CachedParams.Contains(parameter.ActualName))
                        {
                            parameter.AppendCacheKey(b.cacheKeys);
                        }
                    }

                    if (Options.HttpClientOptions.Enabled)
                    {
                        if (parameter.TypeDescriptor.CustomType is not null)
                        {
                            if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                            {
                                b.customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                            }
                        }
                    }
                    if (!parameter.IsVirtual) b.command.Parameters.Add(parameter);

                    if (b.hasNulls is false && parameter.Value == DBNull.Value)
                    {
                        b.hasNulls = true;
                    }

                    if (b.formatter.IsFormattable is false)
                    {
                        if (b.formatter.RefContext)
                        {
                            b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex, b.context));
                            if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex));
                        }
                    }
                    b.paramIndex++;
                    if (b.shouldLog && Options.LogCommandParameters)
                    {
                        var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && b.endpoint.IsAuth ?
                            "***" :
                            FormatParameterForLog(parameter);
                        b.cmdLog!.Append("-- $").Append(b.paramIndex).Append(' ')
                            .Append(parameter.TypeDescriptor.OriginalType)
                            .Append(" = ").AppendLine(p);
                    }
                }
                else
                {
                    StringValues bodyStringValues = b.body;
                    if (TryParseParameter(parameter, ref bodyStringValues, b.endpoint.QueryStringNullHandling))
                    {
                        parameter.ParamType = ParamType.BodyParam;
                        b.hasNulls = false;

                        if (Options.ValidateParameters is not null)
                        {
                            Options.ValidateParameters(new ParameterValidationValues(
                                b.context,
                                b.routine,
                                parameter));
                            if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return false;
                            }
                        }
                        if (Options.ValidateParametersAsync is not null)
                        {
                            await Options.ValidateParametersAsync(new ParameterValidationValues(
                                b.context,
                                b.routine,
                                parameter));
                            if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return false;
                            }
                        }
                        if (b.endpoint.Cached is true && b.endpoint.CachedParams is not null)
                        {
                            if (b.endpoint.CachedParams.Contains(parameter.ConvertedName) || b.endpoint.CachedParams.Contains(parameter.ActualName))
                            {
                                parameter.AppendCacheKey(b.cacheKeys);
                            }
                        }

                        if (Options.HttpClientOptions.Enabled)
                        {
                            if (parameter.TypeDescriptor.CustomType is not null)
                            {
                                if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                                {
                                    b.customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                                }
                            }
                        }
                        if (!parameter.IsVirtual) b.command.Parameters.Add(parameter);
                        if (b.hasNulls is false && parameter.Value == DBNull.Value)
                        {
                            b.hasNulls = true;
                        }

                        if (b.formatter.IsFormattable is false)
                        {
                            if (b.formatter.RefContext)
                            {
                                b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex, b.context));
                                if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex));
                            }
                        }
                        b.paramIndex++;
                        if (b.shouldLog && Options.LogCommandParameters)
                        {
                            var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && b.endpoint.IsAuth ?
                                "***" :
                                FormatParameterForLog(parameter);
                            b.cmdLog!.Append("-- $").Append(b.paramIndex).Append(' ')
                                .Append(parameter.TypeDescriptor.OriginalType)
                                .Append(" = ").AppendLine(p);
                        }
                    }
                }
                continue;
            }

            // header parameter
            if (parameter.Value is null &&
                b.endpoint.RequestHeadersMode == RequestHeadersMode.Parameter &&
                parameter.TypeDescriptor.HasDefault is true &&
                (
                string.Equals(b.endpoint.RequestHeadersParameterName, parameter.ConvertedName, StringComparison.Ordinal) ||
                string.Equals(b.endpoint.RequestHeadersParameterName, parameter.ActualName, StringComparison.Ordinal)
                )
            )
            {
                if (b.queryCollection!.ContainsKey(parameter.ConvertedName) is false)
                {
                    if (b.headers is null)
                    {
                        SearializeHeader(Options, b.context, ref b.headers);
                    }
                    parameter.ParamType = ParamType.HeaderParam;
                    parameter.Value = b.headers;

                    if (Options.ValidateParameters is not null)
                    {
                        Options.ValidateParameters(new ParameterValidationValues(
                            b.context,
                            b.routine,
                            parameter));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    if (Options.ValidateParametersAsync is not null)
                    {
                        await Options.ValidateParametersAsync(new ParameterValidationValues(
                            b.context,
                            b.routine,
                            parameter));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    if (b.endpoint.Cached is true && b.endpoint.CachedParams is not null)
                    {
                        if (b.endpoint.CachedParams.Contains(parameter.ConvertedName) || b.endpoint.CachedParams.Contains(parameter.ActualName))
                        {
                            parameter.AppendCacheKey(b.cacheKeys);
                        }
                    }

                    if (Options.HttpClientOptions.Enabled)
                    {
                        if (parameter.TypeDescriptor.CustomType is not null)
                        {
                            if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                            {
                                b.customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                            }
                        }
                    }
                    if (!parameter.IsVirtual) b.command.Parameters.Add(parameter);

                    if (b.hasNulls is false && parameter.Value == DBNull.Value)
                    {
                        b.hasNulls = true;
                    }

                    if (b.formatter.IsFormattable is false)
                    {
                        if (b.formatter.RefContext)
                        {
                            b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex, b.context));
                            if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex));
                        }
                    }
                    b.paramIndex++;
                    if (b.shouldLog && Options.LogCommandParameters)
                    {
                        var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && b.endpoint.IsAuth ?
                            "***" :
                            FormatParameterForLog(parameter);
                        b.cmdLog!.Append("-- $").Append(b.paramIndex).Append(' ')
                            .Append(parameter.TypeDescriptor.OriginalType)
                            .Append(" = ").AppendLine(p);
                    }

                    continue;
                }
            }

            // path parameter - extract from RouteValues
            if (parameter.Value is null && b.endpoint.HasPathParameters)
            {
                // Use optimized O(1) lookup via HashSet
                string? matchedPathParam = b.endpoint.FindMatchingPathParameter(parameter.ConvertedName, parameter.ActualName);

                // Try to get route value using the path parameter name from the template
                if (matchedPathParam is not null && b.context.Request.RouteValues.TryGetValue(matchedPathParam, out var routeValue))
                {
                    StringValues pathStringValues = routeValue?.ToString() ?? "";
                    if (TryParseParameter(parameter, ref pathStringValues, b.endpoint.QueryStringNullHandling))
                    {
                        parameter.ParamType = ParamType.PathParam;
                        parameter.QueryStringValues = pathStringValues;

                        if (Options.ValidateParameters is not null)
                        {
                            Options.ValidateParameters(new ParameterValidationValues(
                                b.context,
                                b.routine,
                                parameter));
                            if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return false;
                            }
                        }
                        if (Options.ValidateParametersAsync is not null)
                        {
                            await Options.ValidateParametersAsync(new ParameterValidationValues(
                                b.context,
                                b.routine,
                                parameter));
                            if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return false;
                            }
                        }
                        if (b.endpoint.Cached is true && b.endpoint.CachedParams is not null)
                        {
                            if (b.endpoint.CachedParams.Contains(parameter.ConvertedName) || b.endpoint.CachedParams.Contains(parameter.ActualName))
                            {
                                parameter.AppendCacheKey(b.cacheKeys);
                            }
                        }

                        if (Options.HttpClientOptions.Enabled)
                        {
                            if (parameter.TypeDescriptor.CustomType is not null)
                            {
                                if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                                {
                                    b.customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                                }
                            }
                        }
                        if (!parameter.IsVirtual) b.command.Parameters.Add(parameter);

                        if (b.hasNulls is false && parameter.Value == DBNull.Value)
                        {
                            b.hasNulls = true;
                        }

                        if (b.formatter.IsFormattable is false)
                        {
                            if (b.formatter.RefContext)
                            {
                                b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex, b.context));
                                if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex));
                            }
                        }
                        b.paramIndex++;
                        if (b.shouldLog && Options.LogCommandParameters)
                        {
                            var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && b.endpoint.IsAuth ?
                                "***" :
                                FormatParameterForLog(parameter);
                            b.cmdLog!.Append("-- $").Append(b.paramIndex).Append(' ')
                                .Append(parameter.TypeDescriptor.OriginalType)
                                .Append(" = ").AppendLine(p);
                        }

                        continue;
                    }
                }
            }

            if (b.queryCollection!.TryGetValue(parameter.ConvertedName, out var qsValue) is false)
            {
                if (parameter.Value is null)
                {
                    if (Options.ValidateParameters is not null)
                    {
                        Options.ValidateParameters(new ParameterValidationValues(
                            b.context,
                            b.routine,
                            parameter));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    if (Options.ValidateParametersAsync is not null)
                    {
                        await Options.ValidateParametersAsync(new ParameterValidationValues(
                            b.context,
                            b.routine,
                            parameter));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    if (parameter.Value is null)
                    {
                        if (parameter.TypeDescriptor.CustomType is not null)
                        {
                            parameter.Value = DBNull.Value;
                        }
                        else if (IsProxyResponseParameter(b.endpoint, parameter))
                        {
                            // Proxy response parameter - set placeholder value and fall through to add it
                            parameter.Value = DBNull.Value;
                            parameter.ParamType = ParamType.QueryString;
                        }
                        else if (parameter.TypeDescriptor.HasDefault is false)
                        {
                            b.shouldCommit = false;
                            b.uploadHandler?.OnError(b.connection, b.context, null);
                            b.context.Response.StatusCode = StatusCodes.Status404NotFound;
                            await b.context.Response.CompleteAsync();
                            return false;
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

            if (parameter.Value is null && TryParseParameter(parameter, ref qsValue, b.endpoint.QueryStringNullHandling) is false)
            {
                // Check if this is a proxy response parameter - it will be filled in later
                if (IsProxyResponseParameter(b.endpoint, parameter))
                {
                    parameter.Value = DBNull.Value;
                    parameter.ParamType = ParamType.QueryString;
                    // Don't skip - fall through to add the parameter
                }
                else if (parameter.TypeDescriptor.HasDefault is false)
                {
                    b.shouldCommit = false;
                    b.uploadHandler?.OnError(b.connection, b.context, null);
                    b.context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await b.context.Response.CompleteAsync();
                    return false;
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
                    b.context,
                    b.routine,
                    parameter));
                if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                {
                    return false;
                }
            }
            if (Options.ValidateParametersAsync is not null)
            {
                await Options.ValidateParametersAsync(new ParameterValidationValues(
                    b.context,
                    b.routine,
                    parameter));
                if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                {
                    return false;
                }
            }
            if (b.endpoint.Cached is true && b.endpoint.CachedParams is not null)
            {
                if (b.endpoint.CachedParams.Contains(parameter.ConvertedName) || b.endpoint.CachedParams.Contains(parameter.ActualName))
                {
                    parameter.AppendCacheKey(b.cacheKeys);
                }
            }

            if (Options.HttpClientOptions.Enabled)
            {
                if (parameter.TypeDescriptor.CustomType is not null)
                {
                    if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                    {
                        b.customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                    }
                }
            }
            b.command.Parameters.Add(parameter);

            if (b.hasNulls is false && parameter.Value == DBNull.Value)
            {
                b.hasNulls = true;
            }

            if (b.formatter.IsFormattable is false)
            {
                if (b.formatter.RefContext)
                {
                    b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex, b.context));
                    if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                    {
                        return false;
                    }
                }
                else
                {
                    b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex));
                }
            }
            b.paramIndex++;
            if (b.shouldLog && Options.LogCommandParameters)
            {
                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && b.endpoint.IsAuth ?
                    "***" :
                    FormatParameterForLog(parameter);
                b.cmdLog!.Append("-- $").Append(b.paramIndex).Append(' ')
                    .Append(parameter.TypeDescriptor.OriginalType)
                    .Append(" = ").AppendLine(p);
            }
        }

        // Skip query string validation for passthrough proxy endpoints - query will be forwarded as-is
        if (!(b.endpoint.IsProxy && Options.ProxyOptions.Enabled && !b.endpoint.HasProxyResponseParameters))
        {
            foreach (var queryKey in b.queryCollection!.Keys)
            {
                if (b.routine.ParamsHash.Contains(queryKey) is false)
                {
                    b.shouldCommit = false;
                    b.uploadHandler?.OnError(b.connection, b.context, null);
                    b.context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await b.context.Response.CompleteAsync();
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Binds the routine parameters from the JSON body (and path, claims, headers, uploads, resolved values).
    /// Returns false when the request is finished (a response was written). Reads and updates the request state in
    /// <paramref name="b"/>, and may switch the request to an overload (routine, endpoint, formatter).
    /// </summary>
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> BindBodyParametersAsync(RequestState b)
    {
        // Early-return guard: bodyDict is non-null for the rest of this block (later
        // dereferences use `!` where the compiler's flow analysis loses the narrowing).
        if (b.bodyDict is null)
        {
            // Body was present but is not a parseable JSON object - this is a client error,
            // not a routing miss (parse failures are logged above via CouldNotParseJson).
            b.shouldCommit = false;
            b.uploadHandler?.OnError(b.connection, b.context, null);
            b.context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await b.context.Response.CompleteAsync();
            return false;
        }

        // Account for path parameters when counting body parameters
        var pathParamCount = b.endpoint.PathParameters?.Length ?? 0;
        var expectedBodyParamCount = b.routine.ParamCount - pathParamCount;
        if (b.bodyDict.Count != expectedBodyParamCount && overloads.Count > 0)
        {
            if (overloads.TryGetValue(string.Concat(entry.Key, b.bodyDict.Count + pathParamCount), out var overload))
            {
                b.routine = overload.Endpoint.Routine;
                b.endpoint = overload.Endpoint;
                b.formatter = overload.Formatter;
                if (b.formatter.IsFormattable is false)
                {
                    // Reinitialize commandTextBuilder for the new routine
                    b.commandTextBuilder?.Clear();
                    if (b.commandTextBuilder is null)
                    {
                        b.commandTextBuilder = StringBuilderPool.Rent(b.routine.Expression.Length + b.routine.ParamCount * 16);
                    }
                    b.commandTextBuilder.Append(b.routine.Expression);
                }
            }
        }

        for (int i = 0; i < b.routine.Parameters.Length; i++)
        {
            var parameter = b.routine.Parameters[i].NpgsqlRestParameterMemberwiseClone();

            // Resolved parameter: skip HTTP filling, set to DBNull.Value placeholder.
            // The actual value will be resolved via SQL expression after the parameter loop.
            if (b.endpoint.ResolvedParameterExpressions is not null &&
                (b.endpoint.ResolvedParameterExpressions.ContainsKey(parameter.ActualName) ||
                 b.endpoint.ResolvedParameterExpressions.ContainsKey(parameter.ConvertedName)))
            {
                parameter.Value = DBNull.Value;
                if (Options.HttpClientOptions.Enabled)
                {
                    if (parameter.TypeDescriptor.CustomType is not null)
                    {
                        if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                        {
                            b.customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                        }
                    }
                }
                if (!parameter.IsVirtual) b.command.Parameters.Add(parameter);
                b.hasNulls = true;
                if (b.formatter.IsFormattable is false)
                {
                    if (b.formatter.RefContext)
                    {
                        b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex, b.context));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex));
                    }
                }
                b.paramIndex++;
                if (b.shouldLog && Options.LogCommandParameters)
                {
                    b.cmdLog!.Append("-- $").Append(b.paramIndex).Append(' ')
                        .Append(parameter.TypeDescriptor.OriginalType)
                        .Append(" = (resolved)").AppendLine();
                }
                continue;
            }

            if (parameter.HashOf is not null)
            {
                var hashValueBodyDict = b.bodyDict!.GetValueOrDefault(parameter.HashOf.ConvertedName)?.ToString();
                if (string.IsNullOrEmpty(hashValueBodyDict) is true)
                {
                    parameter.Value = DBNull.Value;
                }
                else
                {
                    parameter.Value = Options.AuthenticationOptions.PasswordHasher?.HashPassword(hashValueBodyDict) as object ?? DBNull.Value;
                }
            }
            if (b.endpoint.UseUserParameters is true)
            {
                if (parameter.IsFromUserClaims is true && b.claimsDict is null)
                {
                    b.claimsDict = b.context.User.BuildClaimsDictionary(Options.AuthenticationOptions);
                }
                if (parameter.IsIpAddress is true)
                {
                    parameter.Value = b.context.Request.GetClientIpAddressDbParam();
                }
                else if (b.context.User?.Identity?.IsAuthenticated is true && Options.AuthenticationOptions.ParameterNameClaimsMapping.TryGetValue(parameter.ActualName, out var claimType))
                {
                    // Claim auto-bind always wins (security routines depend on it). If the
                    // request also supplied a value, it would otherwise be silently dropped —
                    // surface that as a WARN so a parameter naming collision is visible.
                    if (b.bodyDict is not null
                        && (b.bodyDict.ContainsKey(parameter.ConvertedName) || b.bodyDict.ContainsKey(parameter.ActualName)))
                    {
                        Logger?.ClaimMappedParamReceivedRequestValue(
                            b.endpoint.Path, parameter.ActualName, "body", claimType);
                    }
                    parameter.Value = b.claimsDict!.GetClaimDbParam(claimType);
                }
                else if (b.context.User?.Identity?.IsAuthenticated is true && parameter.IsUserClaims is true)
                {
                    parameter.Value = b.claimsDict?.GetUserClaimsDbParam() ?? DBNull.Value;
                }
            }
            if (parameter.IsUploadMetadata is true)
            {
                b.uploadMetaParamIndex = b.command.Parameters.Count; // the last one added
                parameter.Value = DBNull.Value;
            }

            // header parameter
            if (parameter.Value is null &&
                b.endpoint.RequestHeadersMode == RequestHeadersMode.Parameter &&
                parameter.TypeDescriptor.HasDefault is true &&
            (
                string.Equals(b.endpoint.RequestHeadersParameterName, parameter.ConvertedName, StringComparison.Ordinal) ||
                string.Equals(b.endpoint.RequestHeadersParameterName, parameter.ActualName, StringComparison.Ordinal)
                )
            )
            {
                if (b.bodyDict!.ContainsKey(parameter.ConvertedName) is false)
                {
                    if (b.headers is null)
                    {
                        SearializeHeader(Options, b.context, ref b.headers);
                    }
                    parameter.ParamType = ParamType.HeaderParam;
                    parameter.Value = b.headers;

                    if (Options.ValidateParameters is not null)
                    {
                        Options.ValidateParameters(new ParameterValidationValues(
                            b.context,
                            b.routine,
                            parameter));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    if (Options.ValidateParametersAsync is not null)
                    {
                        await Options.ValidateParametersAsync(new ParameterValidationValues(
                            b.context,
                            b.routine,
                            parameter));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    if (b.endpoint.Cached is true && b.endpoint.CachedParams is not null)
                    {
                        if (b.endpoint.CachedParams.Contains(parameter.ConvertedName) || b.endpoint.CachedParams.Contains(parameter.ActualName))
                        {
                            parameter.AppendCacheKey(b.cacheKeys);
                        }
                    }

                    if (Options.HttpClientOptions.Enabled)
                    {
                        if (parameter.TypeDescriptor.CustomType is not null)
                        {
                            if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                            {
                                b.customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                            }
                        }
                    }
                    if (!parameter.IsVirtual) b.command.Parameters.Add(parameter);

                    if (b.hasNulls is false && parameter.Value == DBNull.Value)
                    {
                        b.hasNulls = true;
                    }

                    if (b.formatter.IsFormattable is false)
                    {
                        if (b.formatter.RefContext)
                        {
                            b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex, b.context));
                            if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex));
                        }
                    }
                    b.paramIndex++;
                    if (b.shouldLog && Options.LogCommandParameters)
                    {
                        var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && b.endpoint.IsAuth ?
                            "***" :
                            FormatParameterForLog(parameter);
                        b.cmdLog!.Append("-- $").Append(b.paramIndex).Append(' ')
                            .Append(parameter.TypeDescriptor.OriginalType)
                            .Append(" = ").AppendLine(p);
                    }

                    continue;
                }
            }

            // path parameter - extract from RouteValues (for JSON body mode)
            if (parameter.Value is null && b.endpoint.HasPathParameters)
            {
                // Use optimized O(1) lookup via HashSet
                string? matchedPathParam = b.endpoint.FindMatchingPathParameter(parameter.ConvertedName, parameter.ActualName);

                // Try to get route value using the path parameter name from the template
                if (matchedPathParam is not null && b.context.Request.RouteValues.TryGetValue(matchedPathParam, out var routeValue))
                {
                    StringValues pathStringValues = routeValue?.ToString() ?? "";
                    if (TryParseParameter(parameter, ref pathStringValues, b.endpoint.QueryStringNullHandling))
                    {
                        parameter.ParamType = ParamType.PathParam;
                        parameter.QueryStringValues = pathStringValues;

                        if (Options.ValidateParameters is not null)
                        {
                            Options.ValidateParameters(new ParameterValidationValues(
                                b.context,
                                b.routine,
                                parameter));
                            if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return false;
                            }
                        }
                        if (Options.ValidateParametersAsync is not null)
                        {
                            await Options.ValidateParametersAsync(new ParameterValidationValues(
                                b.context,
                                b.routine,
                                parameter));
                            if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                            {
                                return false;
                            }
                        }
                        if (b.endpoint.Cached is true && b.endpoint.CachedParams is not null)
                        {
                            if (b.endpoint.CachedParams.Contains(parameter.ConvertedName) || b.endpoint.CachedParams.Contains(parameter.ActualName))
                            {
                                parameter.AppendCacheKey(b.cacheKeys);
                            }
                        }

                        if (Options.HttpClientOptions.Enabled)
                        {
                            if (parameter.TypeDescriptor.CustomType is not null)
                            {
                                if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                                {
                                    b.customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                                }
                            }
                        }
                        if (!parameter.IsVirtual) b.command.Parameters.Add(parameter);

                        if (b.hasNulls is false && parameter.Value == DBNull.Value)
                        {
                            b.hasNulls = true;
                        }

                        if (b.formatter.IsFormattable is false)
                        {
                            if (b.formatter.RefContext)
                            {
                                b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex, b.context));
                                if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex));
                            }
                        }
                        b.paramIndex++;
                        if (b.shouldLog && Options.LogCommandParameters)
                        {
                            var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && b.endpoint.IsAuth ?
                                "***" :
                                FormatParameterForLog(parameter);
                            b.cmdLog!.Append("-- $").Append(b.paramIndex).Append(' ')
                                .Append(parameter.TypeDescriptor.OriginalType)
                                .Append(" = ").AppendLine(p);
                        }

                        continue;
                    }
                }
            }

            if (b.bodyDict!.TryGetValue(parameter.ConvertedName, out var value) is false)
            {
                if (parameter.Value is null)
                {
                    if (Options.ValidateParameters is not null)
                    {
                        Options.ValidateParameters(new ParameterValidationValues(
                            b.context,
                            b.routine,
                            parameter));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    if (Options.ValidateParametersAsync is not null)
                    {
                        await Options.ValidateParametersAsync(new ParameterValidationValues(
                            b.context,
                            b.routine,
                            parameter));
                        if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                        {
                            return false;
                        }
                    }
                    if (parameter.Value is null)
                    {
                        if (parameter.TypeDescriptor.CustomType is not null)
                        {
                            parameter.Value = DBNull.Value;
                        }
                        else if (IsProxyResponseParameter(b.endpoint, parameter))
                        {
                            // Proxy response parameter - set placeholder value and fall through to add it
                            parameter.Value = DBNull.Value;
                            parameter.ParamType = ParamType.BodyJson;
                        }
                        else if (parameter.TypeDescriptor.HasDefault is false)
                        {
                            b.shouldCommit = false;
                            b.uploadHandler?.OnError(b.connection, b.context, null);
                            b.context.Response.StatusCode = StatusCodes.Status404NotFound;
                            await b.context.Response.CompleteAsync();
                            return false;
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
                if (IsProxyResponseParameter(b.endpoint, parameter))
                {
                    parameter.Value = DBNull.Value;
                    parameter.ParamType = ParamType.BodyJson;
                    // Don't skip - fall through to add the parameter
                }
                else if (parameter.TypeDescriptor.HasDefault is false)
                {
                    b.shouldCommit = false;
                    b.uploadHandler?.OnError(b.connection, b.context, null);
                    b.context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await b.context.Response.CompleteAsync();
                    return false;
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
                    b.context,
                    b.routine,
                    parameter));
                if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                {
                    return false;
                }
            }
            if (Options.ValidateParametersAsync is not null)
            {
                await Options.ValidateParametersAsync(new ParameterValidationValues(
                    b.context,
                    b.routine,
                    parameter));
                if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                {
                    return false;
                }
            }
            if (b.endpoint.Cached is true && b.endpoint.CachedParams is not null)
            {
                if (b.endpoint.CachedParams.Contains(parameter.ConvertedName) || b.endpoint.CachedParams.Contains(parameter.ActualName))
                {
                    parameter.AppendCacheKey(b.cacheKeys);
                }
            }

            if (Options.HttpClientOptions.Enabled)
            {
                if (parameter.TypeDescriptor.CustomType is not null)
                {
                    if (HttpClientTypes.Definitions.ContainsKey(parameter.TypeDescriptor.CustomType))
                    {
                        b.customHttpTypes.Add(parameter.TypeDescriptor.CustomType);
                    }
                }
            }
            b.command.Parameters.Add(parameter);

            if (b.hasNulls is false && parameter.Value == DBNull.Value)
            {
                b.hasNulls = true;
            }

            if (b.formatter.IsFormattable is false)
            {
                if (b.formatter.RefContext)
                {
                    b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex, b.context));
                    if (b.context.Response.HasStarted || b.context.Response.StatusCode != (int)HttpStatusCode.OK)
                    {
                        return false;
                    }
                }
                else
                {
                    b.commandTextBuilder!.Append(b.formatter.AppendCommandParameter(parameter, b.paramIndex));
                }
            }
            b.paramIndex++;
            if (b.shouldLog && Options.LogCommandParameters)
            {
                var p = Options.AuthenticationOptions.ObfuscateAuthParameterLogValues && b.endpoint.IsAuth ?
                    "***" :
                    FormatParameterForLog(parameter);
                b.cmdLog!.Append("-- $").Append(b.paramIndex).Append(' ')
                    .Append(parameter.TypeDescriptor.OriginalType)
                    .Append(" = ").AppendLine(p);
            }
        }

        // Skip body validation for passthrough proxy endpoints - body will be forwarded as-is
        if (!(b.endpoint.IsProxy && Options.ProxyOptions.Enabled && !b.endpoint.HasProxyResponseParameters))
        {
            foreach (var bodyKey in b.bodyDict!.Keys)
            {
                if (b.routine.ParamsHash.Contains(bodyKey) is false)
                {
                    b.shouldCommit = false;
                    b.uploadHandler?.OnError(b.connection, b.context, null);
                    b.context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await b.context.Response.CompleteAsync();
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Everything one request needs, in one object that is reused across requests.
    ///
    /// The request stages (RunAsync) pass this object along instead of dozens of locals and parameters. That keeps the
    /// async frames small - what is suspended while a request waits for the database is the state reference, not a copy
    /// of the request - which matters for memory per request, with or without .NET's Runtime Async.
    ///
    /// Pooled: a request rents one and returns it when it ends, so nothing may keep a reference to it afterwards. In
    /// particular lambdas (the notice handler, cache factories) capture the values they need, never this object.
    /// Field names mirror the locals they replaced.
    /// </summary>
    private sealed class RequestState
    {
        // the request
        public HttpContext context = default!;
        public CancellationToken cancellationToken;
        public IServiceProvider? serviceProvider;

        // the routine the request resolved to (binding may switch to an overload)
        public Routine routine = default!;
        public RoutineEndpoint endpoint = default!;
        public IRoutineSourceParameterFormatter formatter = default!;

        // resources, released by CompleteRequestAsync
        public PipeWriter writer = default!;
        public NpgsqlConnection? connection;
        public NpgsqlTransaction? transaction;
        public NpgsqlCommand command = default!;
        public bool shouldDispose;
        public bool shouldCommit;
        public NoticeEventHandler? noticeHandler;
        public IUploadHandler? uploadHandler;
        public MemoryStream? proxyOutBuffer;
        public Stream? proxyOutOriginalBody;
        public StringBuilder? cmdLog;
        public StringBuilder? cacheKeys;
        public StringBuilder? commandTextBuilder;
        public bool shouldLog;

        // parameter binding
        public List<string> customHttpTypes = default!;
        public IQueryCollection? queryCollection;
        public Dictionary<string, JsonNode?>? bodyDict;
        public string? body;
        public JsonObject? jsonObj;
        public bool hasNulls;
        public int paramIndex;
        public Dictionary<string, object?>? claimsDict;
        public string? headers;
        public int uploadMetaParamIndex;
        public RoutineEndpoint? basicAuthCheckedFor;

        // execution
        public string? commandText;
        public Dictionary<string, string>? customParameters;
        public IRoutineCache? resolvedCache;
        public string? cacheKeyString;
        public bool bypassCache;
        public TimeSpan? cacheTtlOverride;

        // Reused across requests (not part of a request's state): renders rows, see RowRenderer.
        public RowRenderer? rowRenderer;

        private const int MaxPoolSize = 128;
        private static readonly RequestState?[] Pool = new RequestState?[MaxPoolSize];
        private static int _poolIndex = -1; // index of the top occupied slot

        private RequestState() => Reset();

        /// <summary>Rents a state with every field at its initial value.</summary>
        public static RequestState Rent()
        {
            RequestState? state = null;
            int index = Interlocked.Decrement(ref _poolIndex) + 1; // the slot that was on top before the decrement
            if (index >= 0 && index < MaxPoolSize)
            {
                state = Interlocked.Exchange(ref Pool[index], null);
            }
            else
            {
                Interlocked.Increment(ref _poolIndex);
            }
            return state ?? new RequestState();
        }

        /// <summary>Clears the state and returns it to the pool. It must not be used afterwards.</summary>
        public static void Return(RequestState state)
        {
            state.Reset();
            int index = Interlocked.Increment(ref _poolIndex);
            if (index >= 0 && index < MaxPoolSize)
            {
                Pool[index] = state;
            }
            else
            {
                Interlocked.Decrement(ref _poolIndex);
            }
        }

        // Every field, back to the value a new request starts with. A field added above must be added here.
        private void Reset()
        {
            context = default!;
            cancellationToken = default;
            serviceProvider = null;
            routine = default!;
            endpoint = default!;
            formatter = default!;
            writer = default!;
            connection = null;
            transaction = null;
            command = default!;
            shouldDispose = true;
            shouldCommit = true;
            noticeHandler = null;
            uploadHandler = null;
            proxyOutBuffer = null;
            proxyOutOriginalBody = null;
            cmdLog = null;
            cacheKeys = null;
            commandTextBuilder = null;
            shouldLog = false;
            customHttpTypes = default!;
            queryCollection = null;
            bodyDict = null;
            body = null;
            jsonObj = null;
            hasNulls = false;
            paramIndex = 0;
            claimsDict = null;
            headers = null;
            uploadMetaParamIndex = -1;
            basicAuthCheckedFor = null;
            commandText = null;
            customParameters = null;
            resolvedCache = null;
            cacheKeyString = null;
            bypassCache = false;
            cacheTtlOverride = null;
        }
    }

    private static void SetScalarContentType(HttpContext context, TypeDescriptor descriptor)
    {
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
    }

    /// <summary>
    /// True when column bytes can be used as they arrive: they are guaranteed to be valid UTF-8 when the client
    /// encoding is UTF8 (Npgsql's default) and the database is not SQL_ASCII (which stores bytes unvalidated).
    /// Otherwise values go through Npgsql's string decoding.
    /// </summary>
    private static bool IsUtf8Wire(NpgsqlConnection connection)
    {
        try
        {
            var parameters = connection.PostgresParameters;
            return parameters.TryGetValue("client_encoding", out var clientEncoding)
                && string.Equals(clientEncoding, "UTF8", StringComparison.OrdinalIgnoreCase)
                && parameters.TryGetValue("server_encoding", out var serverEncoding)
                && string.Equals(serverEncoding, "SQL_ASCII", StringComparison.OrdinalIgnoreCase) is false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
