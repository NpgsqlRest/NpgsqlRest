using Npgsql;
using NpgsqlRest.Defaults;
using System.Collections.Frozen;
using System.Text;
using NpgsqlRest.HttpClientType;
using NpgsqlRest.UploadHandlers;

namespace NpgsqlRest;

using Metadata = (
    List<NpgsqlRestMetadataEntry> entries,
    FrozenDictionary<string, NpgsqlRestMetadataEntry> overloads,
    bool hasStreamingEvents);

public class NpgsqlRestMetadataEntry
{
    internal NpgsqlRestMetadataEntry(RoutineEndpoint endpoint, IRoutineSourceParameterFormatter formatter, string key)
    {
        Endpoint = endpoint;
        Formatter = formatter;
        Key = key;
    }
    public RoutineEndpoint Endpoint { get; }
    public IRoutineSourceParameterFormatter Formatter { get; }
    public string Key { get; }
}

public static class NpgsqlRestBuilder
{
    public static IApplicationBuilder UseNpgsqlRest(this WebApplication builder, NpgsqlRestOptions options)
    {
        if (options.ConnectionString is null && options.DataSource is null && options.ServiceProviderMode == ServiceProviderObject.None)
        {
            throw new ArgumentException("ConnectionString and DataSource are null and ServiceProviderMode is set to None. You must specify connection with connection string, DataSource object or with ServiceProvider");
        }

        if (options.ConnectionString is not null && options.DataSource is not null && options.ServiceProviderMode == ServiceProviderObject.None)
        {
            throw new ArgumentException("Both ConnectionString and DataSource are provided. Please specify only one.");
        }
        
        Options = options;
        var factory = builder.Services.GetRequiredService<ILoggerFactory>();
        Logger = factory.CreateLogger(options.LoggerName ?? typeof(NpgsqlRestBuilder).Namespace ?? "NpgsqlRest");

        RetryStrategy? defaultStrategy = null;
        if (Options.CommandRetryOptions.Enabled is true && string.IsNullOrEmpty(Options.CommandRetryOptions.DefaultStrategy) is false)
        {
            if (Options.CommandRetryOptions.Strategies
                    .TryGetValue(Options.CommandRetryOptions.DefaultStrategy, out defaultStrategy) is false)
            {
                Logger?.LogWarning("Default retry strategy {defaultStrategy} not found in the list of strategies, command retry strategy will be ignored.",
                    Options.CommandRetryOptions.DefaultStrategy);
            }
        }
        
        if (Options.HttpClientOptions.Enabled is true)
        {
            new HttpClientTypes(builder, defaultStrategy);
        }

        var (
            entries,
            overloads,
            hasStreamingEvents
            ) = Build(builder, defaultStrategy);

        if (entries.Count == 0)
        {
            return builder;
        }

        // Initialize internal request handler for self-referencing calls (bypass HTTP).
        // Store root service provider for creating scoped contexts.
        // The RequestDelegate is set lazily on the first self-call request.
        InternalRequestHandler.Initialize(((IApplicationBuilder)builder).ApplicationServices);

        if (hasStreamingEvents is true)
        {
            builder.UseMiddleware<NpgsqlRestSseEventSource>();
            var lifetime = ((IApplicationBuilder)builder).ApplicationServices.GetService<IHostApplicationLifetime>();
            lifetime?.ApplicationStopping.Register(() =>
            {
                NpgsqlRestSseEventSource.Broadcaster.CompleteAll();
            });
        }

        var internalHandlers = new Dictionary<string, Func<HttpContext, Task>>(entries.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var handler = new NpgsqlRestEndpoint(entry, overloads);
            var endpoint = entry.Endpoint;
            var methodStr = endpoint.Method.ToString();
            var urlInfo = string.Concat(methodStr, " ", endpoint.Path);

            // Register for internal self-calls by method + path (capture handler for direct invocation)
            // Internal-only endpoints are still callable via InternalRequestHandler.
            internalHandlers[$"{methodStr} {endpoint.Path}"] = ctx =>
                handler.InvokeAsync(ctx, ctx.RequestServices, ctx.RequestAborted);

            if (options.CommandTimeout is not null && endpoint.CommandTimeout is null)
            {
                endpoint.CommandTimeout = options.CommandTimeout;
            }

            // Internal-only endpoints skip HTTP route registration — they are only
            // accessible via self-referencing calls (proxy, HTTP client types).
            if (endpoint.InternalOnly)
            {
                if (Options.DebugLogEndpointCreateEvents)
                {
                    Logger?.LogInformation("Internal-only endpoint {UrlInfo} (no HTTP route)", urlInfo);
                }
                continue;
            }

            var routeBuilder = builder.MapMethods(endpoint.Path, [methodStr], handler.InvokeAsync);

            if (endpoint.RateLimiterPolicy is not null || options.DefaultRateLimitingPolicy is not null)
            {
                var policy = endpoint.RateLimiterPolicy ?? options.DefaultRateLimitingPolicy!;
                routeBuilder.RequireRateLimiting(policy);
                Logger?.EndpointEnabledRateLimiterPolicy(urlInfo, policy);
            }

            if (options.RouteHandlerCreated is not null)
            {
                options.RouteHandlerCreated(routeBuilder, endpoint);
            }

            if (Options.DebugLogEndpointCreateEvents)
            {
                Logger?.EndpointCreated(urlInfo);
            }
            if (endpoint.SseEventsPath is not null)
            {
                if (endpoint.SseEventNoticeLevel is null)
                {
                    endpoint.SseEventNoticeLevel = Options.DefaultSseEventNoticeLevel;
                }
                Logger?.EndpointSsePath($"{endpoint.Method} {endpoint.Path}", endpoint.SseEventsPath, endpoint.SseEventNoticeLevel);
            }
        }

        // Register internal handlers for self-referencing calls (bypass HTTP)
        InternalRequestHandler.SetEndpointHandlers(internalHandlers);

        return builder;
    }
    
    private const int MaxPathLength = 2048;

    private static Metadata Build(IApplicationBuilder? builder, RetryStrategy? defaultStrategy)
    {
        // Pre-size dictionaries with reasonable capacity to reduce allocations
        Dictionary<string, NpgsqlRestMetadataEntry> lookup = new(capacity: 128);
        Dictionary<string, NpgsqlRestMetadataEntry> overloads = new(capacity: 16);

        // Create default upload handlers from upload handler options
        Options.UploadOptions.UploadHandlers ??= Options.UploadOptions.CreateUploadHandlers();

        var hasLogin = false;
        if (builder is not null)
        {
            foreach (var handler in Options.EndpointCreateHandlers)
            {
                handler.Setup(builder, Options);
            }
        }

        Options.EndpointSourcesCreated(Options.EndpointSources);
        var hasCachedRoutine = false;
        CommentsMode optionsCommentsMode = Options.CommentsMode;
        bool hasStreamingEvents = false;
        foreach (var source in Options.EndpointSources)
        {
            if (source.CommentsMode.HasValue)
            {
                Options.CommentsMode = source.CommentsMode.Value;
            }
            else
            {
                Options.CommentsMode = optionsCommentsMode;
            }
            
            foreach (var (routine, formatter) in source.Read(builder?.ApplicationServices, defaultStrategy))
            {
                RoutineEndpoint? endpoint = DefaultEndpoint.Create(routine);

                if (endpoint is null)
                {
                    continue;
                }

                if (Options.EndpointCreated is not null)
                {
                    Options.EndpointCreated(endpoint);
                    if (endpoint is null)
                    {
                        continue;
                    }
                }

                // Apply source's NestedJsonForCompositeTypes default if not set by comment annotation
                if (endpoint.NestedJsonForCompositeTypes is null && source.NestedJsonForCompositeTypes)
                {
                    endpoint.NestedJsonForCompositeTypes = true;
                }

                if (defaultStrategy is not null && endpoint.RetryStrategy is null)
                {
                    endpoint.RetryStrategy = defaultStrategy;
                }

                if (endpoint.Path.Length == 0)
                {
                    throw new ArgumentException($"URL path for URL {endpoint.Path}, routine {routine.Name}  is empty.");
                }

                if (endpoint.Path.Length > MaxPathLength)
                {
                    throw new ArgumentException($"URL path for URL {endpoint.Path}, routine {routine.Name} length exceeds {MaxPathLength} characters.");
                }

                // Cache method string to avoid repeated ToString() calls
                var method = endpoint.Method.ToString();
                if (endpoint.HasBodyParameter is true && endpoint.RequestParamType == RequestParamType.BodyJson)
                {
                    endpoint.RequestParamType = RequestParamType.QueryString;
                    Logger?.EndpointTypeChangedBodyParam(method, endpoint.Path, endpoint.BodyParameterName ?? "");
                }
                if (endpoint.Upload is true)
                {
                    if (endpoint.Method != Method.POST)
                    {
                        Logger?.EndpointMethodChangedUpload(method, endpoint.Path, Method.POST.ToString());
                        endpoint.Method = Method.POST;
                        method = "POST"; // Update cached string
                    }
                    if (endpoint.RequestParamType == RequestParamType.BodyJson)
                    {
                        endpoint.RequestParamType = RequestParamType.QueryString;
                        Logger?.EndpointTypeChangedUpload(method, endpoint.Path);
                    }
                }

                var key = string.Concat(method, endpoint.Path);
                var value = new NpgsqlRestMetadataEntry(endpoint, formatter, key);
                if (lookup.TryGetValue(key, out var existing))
                {
                    overloads[string.Concat(key, existing.Endpoint.Routine.ParamCount)] = existing;
                }
                lookup[key] = value;

                // Create cache invalidation endpoint if configured and this endpoint is cached
                if (endpoint.Cached is true && Options.CacheOptions.InvalidateCacheSuffix is not null)
                {
                    var invalidatePath = endpoint.Path.EndsWith('/')
                        ? string.Concat(endpoint.Path, Options.CacheOptions.InvalidateCacheSuffix)
                        : string.Concat(endpoint.Path, "/", Options.CacheOptions.InvalidateCacheSuffix);

                    var invalidateEndpoint = new RoutineEndpoint(
                        routine: routine,
                        path: invalidatePath,
                        method: endpoint.Method,
                        requestParamType: endpoint.RequestParamType,
                        requiresAuthorization: endpoint.RequiresAuthorization,
                        responseContentType: "application/json",
                        responseHeaders: endpoint.ResponseHeaders,
                        requestHeadersMode: endpoint.RequestHeadersMode,
                        requestHeadersParameterName: endpoint.RequestHeadersParameterName,
                        bodyParameterName: endpoint.BodyParameterName,
                        textResponseNullHandling: endpoint.TextResponseNullHandling,
                        queryStringNullHandling: endpoint.QueryStringNullHandling,
                        authorizeRoles: endpoint.AuthorizeRoles,
                        cached: true,
                        cachedParams: endpoint.CachedParams?.ToArray(),
                        connectionName: endpoint.ConnectionName
                    )
                    {
                        InvalidateCache = true,
                        BasicAuth = endpoint.BasicAuth,
                        RateLimiterPolicy = endpoint.RateLimiterPolicy,
                        CacheProfile = endpoint.CacheProfile
                    };

                    var invalidateKey = string.Concat(method, invalidatePath);
                    var invalidateValue = new NpgsqlRestMetadataEntry(invalidateEndpoint, formatter, invalidateKey);
                    lookup[invalidateKey] = invalidateValue;

                    // Notify endpoint create handlers about the invalidation endpoint
                    if (builder is not null)
                    {
                        foreach (var handler in Options.EndpointCreateHandlers)
                        {
                            handler.Handle(invalidateEndpoint);
                        }
                    }
                }

                if (routine.ColumnsTypeDescriptor is not null && routine.ColumnsTypeDescriptor.Length == 1)
                {
                    bool[] unknownResultTypeList = new bool[routine.ColumnsTypeDescriptor.Length];
                    bool hasKnownType = false;
                    for (var i = 0; i < routine.ColumnsTypeDescriptor.Length; i++)
                    {
                        unknownResultTypeList[i] = routine.ColumnsTypeDescriptor[i].ShouldRenderAsUnknownType;
                        if (routine.ColumnsTypeDescriptor[i].ShouldRenderAsUnknownType is false)
                        {
                            hasKnownType = true;
                        }
                    }
                    if (hasKnownType)
                    {
                        routine.UnknownResultTypeList = unknownResultTypeList;
                    }
                }

                if (builder is not null)
                {
                    foreach (var handler in Options.EndpointCreateHandlers)
                    {
                        handler.Handle(endpoint);
                    }
                }

                // Fail-fast if a claim-mapped parameter is declared with a known non-text type.
                // Claim values are strings, so binding to e.g. an Integer parameter would crash
                // every authenticated request with a misleading InvalidCastException deep inside
                // Npgsql ("Writing values of 'System.String' is not supported for parameters
                // having NpgsqlDbType '<X>'"). NpgsqlDbType.Unknown is allowed — Npgsql resolves
                // it server-side, which is the SqlFileSource path where param types aren't
                // inferred. There is no scenario where the known-non-text configuration is valid,
                // so surface it as a hard error at startup rather than letting it ship to prod.
                if (endpoint.UseUserParameters is true && Options.AuthenticationOptions.ParameterNameClaimsMapping.Count > 0)
                {
                    for (int p = 0; p < routine.Parameters.Length; p++)
                    {
                        var param = routine.Parameters[p];
                        if (param.UserClaim is not null
                            && param.TypeDescriptor.IsText is false
                            && param.TypeDescriptor.BaseDbType != NpgsqlTypes.NpgsqlDbType.Unknown)
                        {
                            throw new ArgumentException(
                                $"Endpoint {method} {endpoint.Path} parameter {param.ActualName} is mapped to claim '{param.UserClaim}' but its type is '{param.TypeDescriptor.OriginalType}' which is not text-compatible. Claim values are strings, so binding would fail at runtime with InvalidCastException. Declare the parameter as text/varchar/char/json/jsonb/xml/jsonpath, or remove '{param.ActualName}' from ParameterNameClaimsMapping.");
                        }
                    }
                }

                // Warn if @table_format is set on non-applicable endpoints
                if (Options.TableFormatHandlers is not null
                    && endpoint.CustomParameters is not null
                    && endpoint.CustomParameters.TryGetValue("table_format", out var tableFormatValue))
                {
                    if (!Options.TableFormatHandlers.ContainsKey(tableFormatValue)
                        && !(tableFormatValue.Contains('{') && tableFormatValue.Contains('}')))
                    {
                        Logger?.LogWarning("Endpoint {path} has @table_format = {format} but no table format handler with that name is registered. The annotation will be ignored.",
                            endpoint.Path, tableFormatValue);
                    }
                    else if (routine.IsVoid)
                    {
                        Logger?.LogWarning("Endpoint {path} has @table_format but routine returns void. Table format rendering only applies to set/record results. The annotation will be ignored.",
                            endpoint.Path);
                    }
                    else if (routine.ReturnsSet == false && routine.ColumnCount == 1 && routine.ReturnsRecordType is false)
                    {
                        Logger?.LogWarning("Endpoint {path} has @table_format but routine returns a single scalar value. Table format rendering only applies to set/record results. The annotation will be ignored.",
                            endpoint.Path);
                    }
                }

                if (endpoint.SseEventsPath is not null)
                {
                    if (endpoint.SseEventsPath.StartsWith(endpoint.Path) is false)
                    {
                        // Optimize path concatenation
                        var basePath = endpoint.Path.EndsWith('/') ? endpoint.Path[..^1] : endpoint.Path;
                        var streamPath = endpoint.SseEventsPath.StartsWith('/')
                            ? endpoint.SseEventsPath[1..]
                            : endpoint.SseEventsPath;
                        endpoint.SseEventsPath = string.Concat(basePath, "/", streamPath);
                    }

                    NpgsqlRestSseEventSource.Paths.Add(endpoint.SseEventsPath);
                    hasStreamingEvents = true;
                }

                if (endpoint.Login is true)
                {
                    hasLogin = true;
                    if (routine.IsVoid is true || routine.ReturnsUnnamedSet is true)
                    {
                        throw new ArgumentException($"{routine.Type.ToString().ToLowerInvariant()} {routine.Schema}.{routine.Name} is marked as login and it can't be void or returning unnamed data sets.");
                    }
                }

                if (endpoint.Cached is true && hasCachedRoutine is false)
                {
                    hasCachedRoutine = true;
                }
            }
        }

        if (hasLogin is true)
        {
            if (Options.AuthenticationOptions.DefaultAuthenticationType is null)
            {
                string db = new NpgsqlConnectionStringBuilder(Options.ConnectionString).Database ?? "NpgsqlRest";
                Options.AuthenticationOptions.DefaultAuthenticationType = db;
                Logger?.SetDefaultAuthenticationType(db);
            }
        }

        // Cache profile resolution pass: bind each endpoint's @cache_profile name to the profile's IRoutineCache instance,
        // inherit profile defaults (Expiration, Parameters, When rules) into the endpoint, and verify every referenced
        // profile name actually exists. Unresolved names are accumulated into a single error so users see all typos
        // at once rather than discovering them one rebuild at a time.
        if (Options.CacheOptions.Profiles is not null && Options.CacheOptions.Profiles.Count > 0)
        {
            var usedProfiles = new HashSet<string>(StringComparer.Ordinal);
            var unresolved = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var entry in lookup.Values)
            {
                var endpoint = entry.Endpoint;
                if (endpoint.CacheProfile is null)
                {
                    continue;
                }

                if (Options.CacheOptions.Profiles.TryGetValue(endpoint.CacheProfile, out var profile))
                {
                    usedProfiles.Add(endpoint.CacheProfile);
                    endpoint.ResolvedCache = profile.Cache;
                    endpoint.CacheKeyPrefix = endpoint.CacheProfile;

                    // Annotation wins: endpoint's explicit @cache_expires overrides profile.Expiration.
                    if (endpoint.CacheExpiresIn is null && profile.Expiration is not null)
                    {
                        endpoint.CacheExpiresIn = profile.Expiration;
                    }

                    // Annotation wins: endpoint's @cached p1, p2 (CachedParams non-null) overrides profile.Parameters.
                    // null Parameters → all routine params; [] → URL-only cache; named list → those params only.
                    if (endpoint.CachedParams is null)
                    {
                        if (profile.Parameters is null)
                        {
                            // Profile says "use all params" — populate with every routine parameter name.
                            var all = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var p in endpoint.Routine.Parameters)
                            {
                                all.Add(p.ActualName);
                                if (!string.Equals(p.ConvertedName, p.ActualName, StringComparison.Ordinal))
                                {
                                    all.Add(p.ConvertedName);
                                }
                            }
                            endpoint.CachedParams = all;
                        }
                        else if (profile.Parameters.Length == 0)
                        {
                            // Profile says "URL-only" — empty hash set means no params in the key.
                            endpoint.CachedParams = [];
                        }
                        else
                        {
                            endpoint.CachedParams = [.. profile.Parameters];
                        }
                    }

                    // Validate When rules: each rule's Parameter must be (a) a real routine parameter and
                    // (b) present in the resolved CachedParams (so different rule-evaluations don't collide
                    // on a shared cache entry). Rules failing either check are dropped with a Warning. The
                    // surviving subset is stored on the endpoint for runtime evaluation.
                    if (profile.When is not null && profile.When.Length > 0)
                    {
                        var validRules = new List<CacheWhenRule>(profile.When.Length);
                        foreach (var rule in profile.When)
                        {
                            if (string.IsNullOrWhiteSpace(rule.Parameter))
                            {
                                Logger?.CacheWhenRuleDropped(endpoint.CacheProfile, "(unnamed)", "missing 'Parameter'");
                                continue;
                            }

                            // Is rule.Parameter a real routine parameter?
                            bool isRoutineParam = false;
                            foreach (var rp in endpoint.Routine.Parameters)
                            {
                                if (string.Equals(rp.ActualName, rule.Parameter, StringComparison.Ordinal) ||
                                    string.Equals(rp.ConvertedName, rule.Parameter, StringComparison.Ordinal))
                                {
                                    isRoutineParam = true;
                                    break;
                                }
                            }
                            if (!isRoutineParam)
                            {
                                Logger?.CacheWhenRuleDropped(endpoint.CacheProfile, rule.Parameter,
                                    $"not a parameter of routine {endpoint.Routine.Schema}.{endpoint.Routine.Name}");
                                continue;
                            }

                            // Is rule.Parameter in the resolved cache key set?
                            // CachedParams null is impossible at this point (we set it from profile.Parameters above),
                            // but we'd accept any param name as in-key if it ever were null (= use-all-params).
                            if (endpoint.CachedParams is not null &&
                                !endpoint.CachedParams.Contains(rule.Parameter))
                            {
                                Logger?.CacheWhenRuleDropped(endpoint.CacheProfile, rule.Parameter,
                                    $"not in the cache-key parameter list for endpoint {endpoint.Method} {endpoint.Path}; would cause cache entries to collide across different rule-matches");
                                continue;
                            }

                            validRules.Add(rule);
                        }
                        endpoint.CacheWhen = validRules.Count == 0 ? null : [.. validRules];
                    }

                    Logger?.CacheProfileResolved(
                        endpoint.CacheProfile,
                        string.Concat(endpoint.Method.ToString(), " ", endpoint.Path),
                        endpoint.CacheExpiresIn?.ToString() ?? "(none)",
                        endpoint.CachedParams is null ? "(none)" : string.Join(",", endpoint.CachedParams),
                        endpoint.CacheWhen is null ? "(none)" : string.Join(",", endpoint.CacheWhen.Select(r => r.Parameter)));
                }
                else
                {
                    if (!unresolved.TryGetValue(endpoint.CacheProfile, out var endpoints))
                    {
                        endpoints = new List<string>();
                        unresolved[endpoint.CacheProfile] = endpoints;
                    }
                    endpoints.Add(string.Concat(endpoint.Method.ToString(), " ", endpoint.Path));
                }
            }

            if (unresolved.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Unknown cache profile name(s) referenced by @cache_profile annotation:");
                foreach (var (name, endpoints) in unresolved)
                {
                    sb.Append("  - '").Append(name).Append("' referenced by ").Append(endpoints.Count).AppendLine(" endpoint(s):");
                    foreach (var ep in endpoints)
                    {
                        sb.Append("      ").AppendLine(ep);
                    }
                }
                sb.Append("Available profiles: ").AppendLine(string.Join(", ", Options.CacheOptions.Profiles.Keys));
                throw new InvalidOperationException(sb.ToString());
            }

            // Information-level warning: profile registered but no endpoint references it (likely typo in annotation).
            foreach (var profileName in Options.CacheOptions.Profiles.Keys)
            {
                if (!usedProfiles.Contains(profileName))
                {
                    Logger?.CacheProfileUnused(profileName);
                }
            }
        }
        else
        {
            // No profiles configured but endpoints reference one — fail with a clear message rather than silently
            // ignoring the annotation (which would leave caching half-broken at runtime).
            foreach (var entry in lookup.Values)
            {
                if (entry.Endpoint.CacheProfile is not null)
                {
                    throw new InvalidOperationException(
                        $"Endpoint {entry.Endpoint.Method} {entry.Endpoint.Path} references cache profile " +
                        $"'{entry.Endpoint.CacheProfile}' but no profiles are configured in CacheOptions.Profiles.");
                }
            }
        }

        if (hasCachedRoutine is true && Options.CacheOptions.DefaultRoutineCache is RoutineCache)
        {
            RoutineCache.Start(Options);
            if (builder is WebApplication app)
            {
                app.Lifetime.ApplicationStopping.Register(() =>
                {
                    RoutineCache.Shutdown();
                });
            }
        }

        if (Options.UploadOptions.UploadHandlers is not null && Options.UploadOptions.UploadHandlers.ContainsKey(Options.UploadOptions.DefaultUploadHandler) is false)
        {
            Logger?.LogWarning("Default upload handler {defaultUploadHandler} not found in the list of upload handlers. Using upload endpoint with default handler may cause an error.", 
                Options.UploadOptions.DefaultUploadHandler);
        }

        // Avoid multiple enumerations by creating the list once
        var entries = new List<NpgsqlRestMetadataEntry>(lookup.Values);

        // Create array once if needed by callbacks or handlers
        RoutineEndpoint[]? endpointsArray = null;
        bool arrayPopulated = false;

        if (Options.EndpointsCreated is not null)
        {
            endpointsArray = new RoutineEndpoint[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                endpointsArray[i] = entries[i].Endpoint;
            }
            arrayPopulated = true;
            Options.EndpointsCreated(endpointsArray);
        }

        if (builder is not null)
        {
            foreach (var handler in Options.EndpointCreateHandlers)
            {
                if (endpointsArray is null)
                {
                    endpointsArray = new RoutineEndpoint[entries.Count];
                }
                if (!arrayPopulated)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        endpointsArray[i] = entries[i].Endpoint;
                    }
                    arrayPopulated = true;
                }
                handler.Cleanup(endpointsArray);
                handler.Cleanup();
            }
        }
        
        return (entries, overloads.ToFrozenDictionary(), hasStreamingEvents);
    }
}
