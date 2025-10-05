namespace NpgsqlRest.Defaults;

internal static class DefaultEndpoint
{
    internal static RoutineEndpoint? Create(
        Routine routine,
        NpgsqlRestOptions options,
        ILogger? logger)
    {
        var url = options.UrlPathBuilder(routine, options);
        if (routine.FormatUrlPattern is not null)
        {
            url = string.Format(routine.FormatUrlPattern, url);
        }

        var method = routine.CrudType switch
        {
            CrudType.Select => Method.GET,
            CrudType.Update => Method.POST,
            CrudType.Insert => Method.PUT,
            CrudType.Delete => Method.DELETE,
            _ => Method.POST
        };
        var requestParamType = method == Method.GET || method == Method.DELETE ?
            RequestParamType.QueryString :
            RequestParamType.BodyJson;

        RoutineEndpoint routineEndpoint = new(
                routine,
                path: url,
                method: method,
                requestParamType: requestParamType,
                requiresAuthorization: options.RequiresAuthorization,
                commandTimeout: options.CommandTimeout,
                responseContentType: null,
                responseHeaders: [],
                requestHeadersMode: options.RequestHeadersMode,
                requestHeadersParameterName: options.RequestHeadersParameterName,
                bodyParameterName: null,
                textResponseNullHandling: options.TextResponseNullHandling,
                queryStringNullHandling: options.QueryStringNullHandling,
                userContext: options.AuthenticationOptions.UseUserContext,
                userParameters: options.AuthenticationOptions.UseUserParameters);

        if (options.LogCommands && logger != null)
        {
            routineEndpoint.LogCallback = LoggerMessage.Define<string, string>(LogLevel.Debug,
                new EventId(5, nameof(routineEndpoint.LogCallback)),
                "{parameters}{command}",
                NpgsqlRestLogger.LogDefineOptions);
        }
        else
        {
            routineEndpoint.LogCallback = null;
        }

        if (routine.EndpointHandler is not null)
        {
            var parsed = DefaultCommentParser.Parse(
                routine,
                routineEndpoint,
                options,
                logger);

            return routine.EndpointHandler(parsed);
        }

        return DefaultCommentParser.Parse(
            routine,
            routineEndpoint,
            options, 
            logger);
    }
}