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
            ApplyTsClientModule(routine, parsed);
            return routine.EndpointHandler(parsed);
        }

        var result = DefaultCommentParser.Parse(routine, routineEndpoint);
        ApplyTsClientModule(routine, result);
        return result;
    }

    /// <summary>
    /// Apply TsClient module from Routine.Metadata if no explicit tsclient_module annotation was set.
    /// SQL file source stores the derived module name (from directory structure) in Metadata.
    /// </summary>
    private static void ApplyTsClientModule(Routine routine, RoutineEndpoint? endpoint)
    {
        if (endpoint is null || routine.Metadata is not string moduleName)
        {
            return;
        }

        // Don't override explicit annotation
        if (endpoint.CustomParameters?.ContainsKey("tsclient_module") is true)
        {
            return;
        }

        endpoint.CustomParameters ??= new();
        endpoint.CustomParameters["tsclient_module"] = ToCamelCase(moduleName);
    }

    /// <summary>
    /// Convert a name to camelCase.
    /// "orders" → "orders", "my_orders" → "myOrders", "My-Reports" → "myReports"
    /// </summary>
    private static string ToCamelCase(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        bool capitalizeNext = false;
        bool isFirst = true;

        foreach (var c in name)
        {
            if (c is '_' or '-' or ' ')
            {
                capitalizeNext = true;
                continue;
            }

            if (isFirst)
            {
                sb.Append(char.ToLowerInvariant(c));
                isFirst = false;
            }
            else if (capitalizeNext)
            {
                sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}