using Npgsql;
using NpgsqlRest.Defaults;
using System.Collections.Frozen;
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
        
        var (
            entries,
            overloads,
            hasStreamingEvents
            ) = Build(builder, defaultStrategy);
            
        if (entries.Count == 0)
        {
            return builder;
        }

        if (Options.HttpClientOptions.Enabled is true)
        {
            new HttpClientTypes(builder, defaultStrategy);
        }
        
        if (hasStreamingEvents is true)
        {
            builder.UseMiddleware<NpgsqlRestSseEventSource>();
        }

        foreach (var entry in entries)
        {
            var handler = new NpgsqlRestEndpoint(entry, overloads);
            var endpoint = entry.Endpoint;
            var methodStr = endpoint.Method.ToString();
            var urlInfo = string.Concat(methodStr, " ", endpoint.Path);
            var routeBuilder = builder.MapMethods(endpoint.Path, [methodStr], handler.InvokeAsync);

            if (options.CommandTimeout is not null && endpoint.CommandTimeout is null)
            {
                endpoint.CommandTimeout = options.CommandTimeout;
            }

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

            Logger?.EndpointCreated(urlInfo);
            if (endpoint.SseEventsPath is not null)
            {
                if (endpoint.SseEventNoticeLevel is null)
                {
                    endpoint.SseEventNoticeLevel = Options.DefaultSseEventNoticeLevel;
                }
                Logger?.EndpointSsePath($"{endpoint.Method} {endpoint.Path}", endpoint.SseEventsPath, endpoint.SseEventNoticeLevel);
            }
        }
        
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

        Options.SourcesCreated(Options.RoutineSources);
        var hasCachedRoutine = false;
        CommentsMode optionsCommentsMode = Options.CommentsMode;
        bool hasStreamingEvents = false;
        foreach (var source in Options.RoutineSources)
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
            Logger?.LogError("Default upload handler {defaultUploadHandler} not found in the list of upload handlers. Using upload endpoint with default handler may cause an error.", 
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
