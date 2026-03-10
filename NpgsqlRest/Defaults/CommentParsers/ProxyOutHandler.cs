namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: proxy_out
    /// Syntax: proxy_out [GET | POST | PUT | DELETE | PATCH]
    ///         proxy_out [GET | POST | PUT | DELETE | PATCH] host_url
    ///
    /// Description: Execute the PostgreSQL function first, then forward its result body
    /// to an upstream proxy service. The upstream response is returned to the client.
    /// The original request query string is forwarded to the upstream URL.
    /// </summary>
    private static readonly string[] ProxyOutKey = ["proxy_out", "forward_proxy"];

    private static void HandleProxyOut(
        Routine routine,
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string[] words,
        int len,
        string description)
    {
        if (Options.ProxyOptions.Enabled is false)
        {
            Logger?.LogWarning("proxy_out annotation found but ProxyOptions.Enabled is false. Ignoring proxy_out for {Description}", description);
            return;
        }

        endpoint.IsProxyOut = true;

        if (len >= 2)
        {
            // Check if second word is an HTTP method
            if (Enum.TryParse<Method>(wordsLower[1], true, out var parsedMethod))
            {
                endpoint.ProxyOutMethod = parsedMethod;

                // Check if third word is a URL
                if (len >= 3)
                {
                    var potentialUrl = words[2];
                    if (IsValidUrl(potentialUrl))
                    {
                        endpoint.ProxyOutHost = potentialUrl;
                    }
                }
            }
            else
            {
                // Second word might be a URL
                var potentialUrl = words[1];
                if (IsValidUrl(potentialUrl))
                {
                    endpoint.ProxyOutHost = potentialUrl;
                }
            }
        }

        Logger?.LogInformation(
            "Endpoint {Description} configured as proxy_out to {Host} with method {Method}",
            description,
            endpoint.ProxyOutHost ?? Options.ProxyOptions.Host ?? "(not set)",
            endpoint.ProxyOutMethod?.ToString() ?? "(same as request)");
    }
}
