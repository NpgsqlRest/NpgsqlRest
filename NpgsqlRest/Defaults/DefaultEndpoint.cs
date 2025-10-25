namespace NpgsqlRest.Defaults;

internal static class DefaultEndpoint
{
    internal static RoutineEndpoint? Create(Routine routine)
    {
        var url = Options.UrlPathBuilder(routine, Options);
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
                requiresAuthorization: Options.RequiresAuthorization,
                commandTimeout: Options.CommandTimeout,
                responseContentType: null,
                responseHeaders: [],
                requestHeadersMode: Options.RequestHeadersMode,
                requestHeadersParameterName: Options.RequestHeadersParameterName,
                bodyParameterName: null,
                textResponseNullHandling: Options.TextResponseNullHandling,
                queryStringNullHandling: Options.QueryStringNullHandling,
                userContext: Options.AuthenticationOptions.UseUserContext,
                userParameters: Options.AuthenticationOptions.UseUserParameters);

        if (Options.LogCommands && Logger != null)
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
            var parsed = DefaultCommentParser.Parse(routine, routineEndpoint);

            return routine.EndpointHandler(parsed);
        }

        return DefaultCommentParser.Parse(routine, routineEndpoint);
    }
}