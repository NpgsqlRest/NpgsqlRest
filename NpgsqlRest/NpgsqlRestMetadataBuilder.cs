using Npgsql;
using NpgsqlRest.Defaults;
using System.Collections.Frozen;
using NpgsqlRest.UploadHandlers;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest;

using Metadata = (
    List<NpgsqlRestMetadataEntry> entries,
    FrozenDictionary<string, NpgsqlRestMetadataEntry> overloads,
    bool hasStreamingEvents);

public static class NpgsqlRestMetadataBuilder
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

        // Set the static instance
        Options = options;

        var factory = builder.Services.GetRequiredService<ILoggerFactory>();
        ILogger? logger = factory.CreateLogger(options.LoggerName ?? typeof(NpgsqlRestMetadataBuilder).Namespace ?? "NpgsqlRest");

        var (entries,overloads, hasStreamingEvents) =
            NpgsqlRestMetadataBuilder.Build(logger, builder);
        if (entries.Count == 0)
        {
            return builder;
        }
        
        if (hasStreamingEvents is true)
        {
            builder.UseMiddleware<NpgsqlRestNoticeEventSource>();
        }
        
        foreach (var entry in entries)
        {
            var handler = new NpgsqlRestEndpoint(entry, overloads, logger);
            var routeBuilder = builder.MapMethods(entry.Endpoint.Path, [entry.Endpoint.Method.ToString()], handler.InvokeAsync);
            
            if (options.RouteHandlerCreated is not null)
            {
                options.RouteHandlerCreated(routeBuilder, entry.Endpoint);
            }
            
            if (options.LogEndpointCreatedInfo)
            {
                var urlInfo = string.Concat(entry.Endpoint.Method, " ", entry.Endpoint.Path);
                logger?.EndpointCreated(urlInfo);
                if (entry.Endpoint.InfoEventsStreamingPath is not null)
                {
                    logger?.EndpointInfoStreamingPath(urlInfo, entry.Endpoint.InfoEventsStreamingPath);
                }
            }
        }
        
        return builder;
    }
    
    private const int MaxPathLength = 2048;

    private static Metadata Build(ILogger? logger, IApplicationBuilder? builder)
    {
        Dictionary<string, NpgsqlRestMetadataEntry> lookup = [];
        Dictionary<string, NpgsqlRestMetadataEntry> overloads = [];

        // Create default upload handlers from upload handler options
        Options.UploadOptions.UploadHandlers ??= Options.UploadOptions.CreateUploadHandlers();

        var hasLogin = false;
        if (builder is not null)
        {
            foreach (var handler in Options.EndpointCreateHandlers)
            {
                handler.Setup(builder, logger, Options);
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
            
            RetryStrategy? defaultStrategy = null;
            if (Options.CommandRetryOptions.Enabled is true && string.IsNullOrEmpty(Options.CommandRetryOptions.DefaultStrategy) is false)
            {
                if (Options.CommandRetryOptions.Strategies
                        .TryGetValue(Options.CommandRetryOptions.DefaultStrategy, out defaultStrategy) is false)
                {
                    logger?.LogWarning("Default retry strategy {defaultStrategy} not found in the list of strategies, command retry strategy will be ignored.",
                        Options.CommandRetryOptions.DefaultStrategy);
                }
            }
            
            foreach (var (routine, formatter) in source.Read(builder?.ApplicationServices, defaultStrategy, logger))
            {
                RoutineEndpoint endpoint = DefaultEndpoint.Create(routine, logger)!;

                if (endpoint is null)
                {
                    continue;
                }
                
                if (Options.EndpointCreated is not null)
                {
                    Options.EndpointCreated(endpoint);
                }

                if (endpoint is null)
                {
                    continue;
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

                var method = endpoint.Method.ToString();
                if (endpoint.HasBodyParameter is true && endpoint.RequestParamType == RequestParamType.BodyJson)
                {
                    endpoint.RequestParamType = RequestParamType.QueryString;
                    logger?.EndpointTypeChangedBodyParam(method, endpoint.Path, endpoint!.BodyParameterName ?? "");
                }
                if (endpoint.Upload is true)
                {
                    if (endpoint.Method != Method.POST)
                    {
                        logger?.EndpointMethodChangedUpload(method, endpoint.Path, Method.POST.ToString());
                        endpoint.Method = Method.POST;
                    }
                    if (endpoint.RequestParamType == RequestParamType.BodyJson)
                    {
                        endpoint.RequestParamType = RequestParamType.QueryString;
                        logger?.EndpointTypeChangedUpload(method, endpoint.Path);
                    }
                }

                var key = string.Concat(method, endpoint?.Path);
                var value = new NpgsqlRestMetadataEntry(endpoint!, formatter, key);
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
                        handler.Handle(endpoint!);
                    }
                }

                if (endpoint?.InfoEventsStreamingPath is not null)
                {
                    if (endpoint.InfoEventsStreamingPath.StartsWith(endpoint.Path) is false)
                    {
                        endpoint.InfoEventsStreamingPath = string.Concat(
                            endpoint.Path.EndsWith('/') ? endpoint.Path[..^1] : endpoint.Path , "/", 
                            endpoint.InfoEventsStreamingPath.StartsWith('/') ? endpoint.InfoEventsStreamingPath[1..] : endpoint.InfoEventsStreamingPath);
                    }

                    NpgsqlRestNoticeEventSource.Paths.Add(endpoint.InfoEventsStreamingPath);

                    if (hasStreamingEvents is false)
                    {
                        hasStreamingEvents = true;
                    }
                }
                
                if (endpoint?.Login is true)
                {
                    if (hasLogin is false)
                    {
                        hasLogin = true;
                    }
                    if (routine.IsVoid is true || routine.ReturnsUnnamedSet is true)
                    {
                        throw new ArgumentException($"{routine.Type.ToString().ToLowerInvariant()} {routine.Schema}.{routine.Name} is marked as login and it can't be void or returning unnamed data sets.");
                    }
                }

                if (endpoint?.Cached is true && hasCachedRoutine is false)
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
                logger?.SetDefaultAuthenticationType(db);
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
            logger?.LogError("Default upload handler {defaultUploadHandler} not found in the list of upload handlers. Using upload endpoint with default handler may cause an error.", 
                Options.UploadOptions.DefaultUploadHandler);
        }

        var entries = lookup.Values.ToList();
        if (Options.EndpointsCreated is not null)
        {
            Options.EndpointsCreated([.. entries.Select(x => x.Endpoint)]);
        }

        if (builder is not null)
        {
            RoutineEndpoint[]? array = null;
            foreach (var handler in Options.EndpointCreateHandlers)
            {
                array ??= [.. entries.Select(x => x.Endpoint)];
                handler.Cleanup(array);
                handler.Cleanup();
            }
        }
        
        return (entries, overloads.ToFrozenDictionary(), hasStreamingEvents);
    }
}
