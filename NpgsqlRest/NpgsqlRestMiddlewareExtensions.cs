using System.Diagnostics.CodeAnalysis;
using System.Net;
using static System.Net.Mime.MediaTypeNames;

namespace NpgsqlRest;

public static class NpgsqlRestMiddlewareExtensions
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
        ILogger? logger = null;
        if (options.Logger is not null)
        {
            logger = options.Logger;
        }
        else if (builder is WebApplication app)
        {
            var factory = app.Services.GetRequiredService<ILoggerFactory>();
            logger = factory is not null ? factory.CreateLogger(options.LoggerName ?? typeof(NpgsqlRestMiddlewareExtensions).Namespace ?? "NpgsqlRest") : app.Logger;
        }

        var (entries,overloads, hasStreamingEvents) = 
            NpgsqlRestMetadataBuilder.Build(options, logger, builder);
        if (entries.Count == 0)
        {
            return builder;
        }
        
        if (hasStreamingEvents is true)
        {
            // todo: revert to endpoint 
            NpgsqlRestNoticeEventSource.SetOptions(options, logger);
            builder.UseMiddleware<NpgsqlRestNoticeEventSource>();
        }
        
        foreach (var entry in entries)
        {
            var handler = new NpgsqlRestEndpoint(entry, options, overloads, logger);
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
}