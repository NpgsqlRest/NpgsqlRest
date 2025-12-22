namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: proxy
    /// Syntax: proxy
    ///         proxy [GET | POST | PUT | DELETE | PATCH]
    ///         proxy host_url
    ///         proxy [GET | POST | PUT | DELETE | PATCH] host_url
    ///
    /// Description: Configure this endpoint as a reverse proxy.
    /// - Use 'proxy' alone to forward to ProxyOptions.Host with same HTTP method.
    /// - Optionally specify HTTP method to override the proxy request method.
    /// - Optionally specify host URL to override ProxyOptions.Host for this endpoint.
    /// </summary>
    private static readonly string[] ProxyKey = ["proxy", "reverse_proxy"];

    private static void HandleProxy(
        Routine routine,
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string[] words,
        int len,
        string description)
    {
        if (Options.ProxyOptions.Enabled is false)
        {
            Logger?.LogWarning("Proxy annotation found but ProxyOptions.Enabled is false. Ignoring proxy for {Description}", description);
            return;
        }

        endpoint.IsProxy = true;

        if (len >= 2)
        {
            // Check if second word is an HTTP method
            if (Enum.TryParse<Method>(wordsLower[1], true, out var parsedMethod))
            {
                endpoint.ProxyMethod = parsedMethod;

                // Check if third word is a URL
                if (len >= 3)
                {
                    var potentialUrl = words[2];
                    if (IsValidUrl(potentialUrl))
                    {
                        endpoint.ProxyHost = potentialUrl;
                    }
                }
            }
            else
            {
                // Second word might be a URL
                var potentialUrl = words[1];
                if (IsValidUrl(potentialUrl))
                {
                    endpoint.ProxyHost = potentialUrl;
                }
            }
        }

        // Detect proxy response parameters
        DetectProxyResponseParameters(routine, endpoint);

        Logger?.LogInformation("Endpoint {Description} configured as proxy to {Host} with method {Method}. HasProxyResponseParameters: {HasParams}",
            description,
            endpoint.ProxyHost ?? Options.ProxyOptions.Host ?? "(not set)",
            endpoint.ProxyMethod?.ToString() ?? "(same as request)",
            endpoint.HasProxyResponseParameters);
    }

    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static void DetectProxyResponseParameters(Routine routine, RoutineEndpoint endpoint)
    {
        var proxyOptions = Options.ProxyOptions;
        var proxyParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            proxyOptions.ResponseStatusCodeParameter,
            proxyOptions.ResponseBodyParameter,
            proxyOptions.ResponseHeadersParameter,
            proxyOptions.ResponseContentTypeParameter,
            proxyOptions.ResponseSuccessParameter,
            proxyOptions.ResponseErrorMessageParameter
        };

        foreach (var param in routine.Parameters)
        {
            // Check both ActualName and ConvertedName
            var paramName = param.ActualName ?? param.ConvertedName;
            if (paramName is not null && proxyParamNames.Contains(paramName))
            {
                endpoint.HasProxyResponseParameters = true;
                endpoint.ProxyResponseParameterNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                endpoint.ProxyResponseParameterNames.Add(paramName);
            }
        }
    }
}
