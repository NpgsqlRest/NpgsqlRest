﻿using NpgsqlRest.UploadHandlers.Handlers;

namespace NpgsqlRest.UploadHandlers;

public static class UploadExtensions
{
    public static Dictionary<string, Func<ILogger?, IUploadHandler>>? CreateUploadHandlers(this NpgsqlRestUploadOptions options)
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
        var result = new Dictionary<string, Func<ILogger?, IUploadHandler>>();
        if (options.DefaultUploadHandlerOptions.LargeObjectEnabled)
        {
            result.Add(options.DefaultUploadHandlerOptions.LargeObjectKey, logger => new LargeObjectUploadHandler(options, logger));
        }
        if (options.DefaultUploadHandlerOptions.FileSystemEnabled)
        {
            result.Add(options.DefaultUploadHandlerOptions.FileSystemKey, logger => new FileSystemUploadHandler(options, logger));
        }
        if (options.DefaultUploadHandlerOptions.CsvUploadEnabled)
        {
            result.Add(options.DefaultUploadHandlerOptions.CsvUploadKey, logger => new CsvUploadHandler(options, logger));
        }
        return result;
    }

    public static IUploadHandler? CreateUploadHandler(this NpgsqlRestOptions options, RoutineEndpoint endpoint, ILogger? logger)
    {
        if (endpoint.UploadHandlers is null || endpoint.UploadHandlers.Length == 0)
        {
            if (options.UploadOptions.UploadHandlers is not null && options.UploadOptions.UploadHandlers.TryGetValue(options.UploadOptions.DefaultUploadHandler, out var handler))
            {
                return new DefaultUploadHandler(options.UploadOptions, [handler(logger).SetType(options.UploadOptions.DefaultUploadHandler)]);
            }
            else
            {
                throw new Exception($"Default upload handler '{options.UploadOptions.DefaultUploadHandler}' not found.");
            }
        }
        else if (endpoint.UploadHandlers.Length == 1)
        { 
            var handlerName = endpoint.UploadHandlers[0];
            if (options.UploadOptions.UploadHandlers is not null && options.UploadOptions.UploadHandlers.TryGetValue(handlerName, out var handler))
            {
                return new DefaultUploadHandler(options.UploadOptions, [handler(logger).SetType(handlerName)]);
            }
            else
            {
                throw new Exception($"Upload handler '{handlerName}' not found.");
            }
        }
        else
        {
            // all handlers defined
            List<IUploadHandler> handlers = new(endpoint.UploadHandlers.Length);
            foreach (var handlerName in endpoint.UploadHandlers)
            {
                if (options.UploadOptions.UploadHandlers is not null && options.UploadOptions.UploadHandlers.TryGetValue(handlerName, out var handler))
                {
                    handlers.Add(handler(logger).SetType(handlerName));
                }
                else
                {
                    throw new Exception($"Upload handler '{handlerName}' not found.");
                }
            }
            return new DefaultUploadHandler(options.UploadOptions, [.. handlers]);
        }
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
