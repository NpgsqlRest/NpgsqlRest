﻿using NpgsqlRest.UploadHandlers.Handlers;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest.UploadHandlers;

public static class UploadExtensions
{
    public static Dictionary<string, Func<RetryStrategy?, ILogger?, IUploadHandler>>? CreateUploadHandlers(this NpgsqlRestUploadOptions options)
    {
        if (options is null)
        {
            return null;
        }
        if (options.Enabled is false)
        {
            return null;
        }
        if (options.DefaultUploadHandlerOptions.LargeObjectEnabled is false && 
            options.DefaultUploadHandlerOptions.FileSystemEnabled is false && 
            options.DefaultUploadHandlerOptions.CsvUploadEnabled is false)
        {
            return null;
        }
        var result = new Dictionary<string, Func<RetryStrategy?, ILogger?, IUploadHandler>>();
        if (options.DefaultUploadHandlerOptions.LargeObjectEnabled)
        {
            result.Add(options.DefaultUploadHandlerOptions.LargeObjectKey, (strategy, logger) => new LargeObjectUploadHandler(strategy, logger));
        }
        if (options.DefaultUploadHandlerOptions.FileSystemEnabled)
        {
            result.Add(options.DefaultUploadHandlerOptions.FileSystemKey, (strategy, logger) => new FileSystemUploadHandler(logger));
        }
        if (options.DefaultUploadHandlerOptions.CsvUploadEnabled)
        {
            result.Add(options.DefaultUploadHandlerOptions.CsvUploadKey, (strategy, logger) => new CsvUploadHandler(strategy, logger));
        }
        return result;
    }

    public static IUploadHandler? CreateUploadHandler(this RoutineEndpoint endpoint, ILogger? logger)
    {
        if (endpoint.UploadHandlers is null || endpoint.UploadHandlers.Length == 0)
        {
            if (Options.UploadOptions.UploadHandlers is not null && 
                Options.UploadOptions.UploadHandlers.TryGetValue(Options.UploadOptions.DefaultUploadHandler, out var handler))
            {
                return new DefaultUploadHandler(Options.UploadOptions, [handler(endpoint.RetryStrategy, logger).SetType(Options.UploadOptions.DefaultUploadHandler)]);
            }

            throw new Exception($"Default upload handler '{Options.UploadOptions.DefaultUploadHandler}' not found.");
        }

        if (endpoint.UploadHandlers.Length == 1)
        { 
            var handlerName = endpoint.UploadHandlers[0];
            if (Options.UploadOptions.UploadHandlers is not null && 
                Options.UploadOptions.UploadHandlers.TryGetValue(handlerName, out var handler))
            {
                return new DefaultUploadHandler(Options.UploadOptions, [handler(endpoint.RetryStrategy, logger).SetType(handlerName)]);
            }

            throw new Exception($"Upload handler '{handlerName}' not found.");
        }

        // all handlers defined
        List<IUploadHandler> handlers = new(endpoint.UploadHandlers.Length);
        foreach (var handlerName in endpoint.UploadHandlers)
        {
            if (Options.UploadOptions.UploadHandlers is not null && 
                Options.UploadOptions.UploadHandlers.TryGetValue(handlerName, out var handler))
            {
                handlers.Add(handler(endpoint.RetryStrategy, logger).SetType(handlerName));
            }
            else
            {
                throw new Exception($"Upload handler '{handlerName}' not found.");
            }
        }
        return new DefaultUploadHandler(Options.UploadOptions, [.. handlers]);
    }

    public static string[]? SplitParameter(this string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }
        return type.Split(',', ' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
