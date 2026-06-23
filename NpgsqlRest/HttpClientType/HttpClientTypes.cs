using System.Collections.Frozen;
using Npgsql;

namespace NpgsqlRest.HttpClientType;

public class HttpClientTypes
{
    private const string TypeQuery = @"select
    (quote_ident(n.nspname) || '.' || quote_ident(t.typname))::regtype::text as name,
    des.description as comment,
    array_agg(quote_ident(a.attname)) as att_names
from 
    pg_catalog.pg_type t
    join pg_catalog.pg_namespace n on n.oid = t.typnamespace
    join pg_catalog.pg_class c on t.typrelid = c.oid and c.relkind = 'c'
    join pg_catalog.pg_attribute a on t.typrelid = a.attrelid and a.attisdropped is false
    join pg_catalog.pg_description des on t.oid = des.objoid
where
    nspname not like 'pg_%'
    and nspname <> 'information_schema'
    and des.description is not null
    and a.attnum > 0
group by n.nspname, t.typname, des.description";

    public static FrozenDictionary<string, HttpTypeDefinition> Definitions { get; private set; } = FrozenDictionary<string, HttpTypeDefinition>.Empty;
    public static bool NeedsParsing { get; private set; }

    public HttpClientTypes(IApplicationBuilder? builder, RetryStrategy? retryStrategy)
    {
        bool shouldDispose = true;
        NpgsqlConnection? connection = null;
        try
        {
            Options.CreateAndOpenSourceConnection(builder?.ApplicationServices, ref connection, ref shouldDispose, "HttpClientTypes");

            if (connection is null)
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.CommandText = TypeQuery;
            command.LogCommand(nameof(HttpClientTypes));
            using NpgsqlDataReader reader = command.ExecuteReaderWithRetry(retryStrategy);

            var definitions = new Dictionary<string, HttpTypeDefinition>();
            bool needsParsing = false;

            while (reader.Read())
            {
                string typeName = reader.GetString(0);
                string? comment = reader.IsDBNull(1) ? null : reader.GetString(1);
                string[] attNames = reader.IsDBNull(2) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(2);
                if (string.IsNullOrWhiteSpace(comment))
                {
                    continue;
                }

                var typeDefinition = ParseHttpTypeDefinition(comment, typeName);
                if (typeDefinition is not null)
                {
                    definitions[typeName] = typeDefinition;
                    if (typeDefinition.NeedsParsing)
                    {
                        needsParsing = true;
                    }
                }
            }

            Definitions = definitions.ToFrozenDictionary();
            NeedsParsing = needsParsing;
        }
        finally
        {
            if (connection is not null && shouldDispose is true)
            {
                connection.Dispose();
            }
        }
    }
    
    public HttpTypeDefinition? ParseHttpTypeDefinition(string comment, string? typeName = null)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var span = comment.AsSpan();
        int pos = 0;
        TimeSpan? timeout = null;
        TimeSpan[]? retryDelays = null;
        HashSet<int>? retryOnStatusCodes = null;
        bool cacheEnabled = false;
        TimeSpan? cacheDuration = null;

        // Parse directives before the request line
        while (pos < span.Length)
        {
            int lineEnd = span[pos..].IndexOfAny('\r', '\n');
            var line = lineEnd == -1 ? span[pos..] : span[pos..(pos + lineEnd)];
            var trimmedLine = TrimSpan(line);

            // Skip empty lines
            if (trimmedLine.IsEmpty)
            {
                pos += lineEnd == -1 ? line.Length : lineEnd;
                if (pos < span.Length && span[pos] == '\r') pos++;
                if (pos < span.Length && span[pos] == '\n') pos++;
                continue;
            }

            // Check for timeout directive (with or without # prefix)
            if (TryParseTimeoutDirective(trimmedLine, typeName, out var parsedTimeout))
            {
                timeout = parsedTimeout;
                pos += lineEnd == -1 ? line.Length : lineEnd;
                if (pos < span.Length && span[pos] == '\r') pos++;
                if (pos < span.Length && span[pos] == '\n') pos++;
                continue;
            }

            // Check for retry_delay directive
            if (TryParseRetryDirective(trimmedLine, typeName, out var parsedDelays, out var parsedRetryCodes))
            {
                retryDelays = parsedDelays;
                retryOnStatusCodes = parsedRetryCodes;
                pos += lineEnd == -1 ? line.Length : lineEnd;
                if (pos < span.Length && span[pos] == '\r') pos++;
                if (pos < span.Length && span[pos] == '\n') pos++;
                continue;
            }

            // Check for cache directive
            if (TryParseCacheDirective(trimmedLine, typeName, out var parsedCacheDuration))
            {
                cacheEnabled = true;
                cacheDuration = parsedCacheDuration;
                pos += lineEnd == -1 ? line.Length : lineEnd;
                if (pos < span.Length && span[pos] == '\r') pos++;
                if (pos < span.Length && span[pos] == '\n') pos++;
                continue;
            }

            // Check for # comment (non-timeout directive)
            if (trimmedLine[0] == '#')
            {
                pos += lineEnd == -1 ? line.Length : lineEnd;
                if (pos < span.Length && span[pos] == '\r') pos++;
                if (pos < span.Length && span[pos] == '\n') pos++;
                continue;
            }

            // Not a directive, must be the request line
            break;
        }

        // Find first line (request line)
        int firstLineEnd = span[pos..].IndexOfAny('\r', '\n');
        var firstLine = firstLineEnd == -1 ? span[pos..] : span[pos..(pos + firstLineEnd)];
        firstLine = TrimSpan(firstLine);

        if (firstLine.IsEmpty)
        {
            Logger?.LogWarning("Type '{TypeName}': HTTP type definition has no request line (expected 'METHOD URL')", typeName);
            return null;
        }

        // Parse request line: METHOD URL [HTTP/version]
        int firstSpace = firstLine.IndexOf(' ');
        if (firstSpace <= 0)
        {
            Logger?.LogWarning("Type '{TypeName}': HTTP type definition request line is missing URL: '{RequestLine}'", typeName, new string(firstLine));
            return null;
        }

        var methodSpan = firstLine[..firstSpace];

        // Fast uppercase check and validation (most methods are already uppercase)
        Span<char> methodUpper = stackalloc char[methodSpan.Length];
        for (int i = 0; i < methodSpan.Length; i++)
        {
            methodUpper[i] = char.ToUpperInvariant(methodSpan[i]);
        }

        if (!IsValidHttpMethod(methodUpper))
        {
            Logger?.LogWarning("Type '{TypeName}': HTTP type definition has invalid HTTP method '{Method}'. Supported methods: GET, POST, PUT, PATCH, DELETE", typeName, new string(methodSpan));
            return null;
        }

        // Find URL (skip spaces after method)
        var afterMethod = firstLine[(firstSpace + 1)..];
        afterMethod = TrimSpan(afterMethod);
        if (afterMethod.IsEmpty)
        {
            Logger?.LogWarning("Type '{TypeName}': HTTP type definition is missing URL after method '{Method}'", typeName, new string(methodUpper));
            return null;
        }

        // URL ends at space (before HTTP/version) or end of line
        int urlEnd = afterMethod.IndexOf(' ');
        var urlSpan = urlEnd == -1 ? afterMethod : afterMethod[..urlEnd];

        // Check URL for placeholders
        bool needsParsing = ContainsPlaceholder(urlSpan);

        var result = new HttpTypeDefinition
        {
            Method = new string(methodUpper),
            Url = new string(urlSpan),
            Timeout = timeout,
            RetryDelays = retryDelays,
            RetryOnStatusCodes = retryOnStatusCodes,
            CacheEnabled = cacheEnabled,
            CacheDuration = cacheDuration
        };

        // Move past first line
        if (firstLineEnd == -1)
        {
            NormalizeCacheDirective(result, typeName);
            result.NeedsParsing = needsParsing;
            return result;
        }

        pos += firstLineEnd;
        // Skip \r\n or \n
        if (pos < span.Length && span[pos] == '\r') pos++;
        if (pos < span.Length && span[pos] == '\n') pos++;

        Dictionary<string, string>? headers = null;
        int bodyStart = -1;

        // Parse headers
        while (pos < span.Length)
        {
            // Find end of current line
            int lineEnd = span[pos..].IndexOfAny('\r', '\n');
            var line = lineEnd == -1 ? span[pos..] : span[pos..(pos + lineEnd)];

            // Empty line indicates end of headers
            if (line.IsEmpty || line.IsWhiteSpace())
            {
                pos += lineEnd == -1 ? line.Length : lineEnd;
                if (pos < span.Length && span[pos] == '\r') pos++;
                if (pos < span.Length && span[pos] == '\n') pos++;
                bodyStart = pos;
                break;
            }

            // Directives may also appear in the header section (after the request line), not only
            // before it. Check for them before treating the line as an HTTP header. A directive's
            // separator/value shape means real headers (which carry a 'Name-Word: value') don't match.
            if (TryParseTimeoutDirective(line, typeName, out var hdrTimeout))
            {
                result.Timeout = hdrTimeout;
                pos += lineEnd == -1 ? line.Length : lineEnd;
                if (pos < span.Length && span[pos] == '\r') pos++;
                if (pos < span.Length && span[pos] == '\n') pos++;
                continue;
            }
            if (TryParseRetryDirective(line, typeName, out var hdrDelays, out var hdrCodes))
            {
                result.RetryDelays = hdrDelays;
                result.RetryOnStatusCodes = hdrCodes;
                pos += lineEnd == -1 ? line.Length : lineEnd;
                if (pos < span.Length && span[pos] == '\r') pos++;
                if (pos < span.Length && span[pos] == '\n') pos++;
                continue;
            }
            if (TryParseCacheDirective(line, typeName, out var hdrCacheDuration))
            {
                result.CacheEnabled = true;
                result.CacheDuration = hdrCacheDuration;
                pos += lineEnd == -1 ? line.Length : lineEnd;
                if (pos < span.Length && span[pos] == '\r') pos++;
                if (pos < span.Length && span[pos] == '\n') pos++;
                continue;
            }

            int colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var headerName = TrimSpan(line[..colonIndex]);
                var headerValue = TrimSpan(line[(colonIndex + 1)..]);

                if (!headerName.IsEmpty)
                {
                    // Check header value for placeholders
                    if (!needsParsing && ContainsPlaceholder(headerValue))
                    {
                        needsParsing = true;
                    }

                    if (headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        result.ContentType = new string(headerValue);
                    }
                    else
                    {
                        headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        headers[new string(headerName)] = new string(headerValue);
                    }
                }
            }

            pos += lineEnd == -1 ? line.Length : lineEnd;
            if (pos < span.Length && span[pos] == '\r') pos++;
            if (pos < span.Length && span[pos] == '\n') pos++;
        }

        if (headers is { Count: > 0 })
        {
            result.Headers = headers;
        }

        // Parse body
        if (bodyStart >= 0 && bodyStart < span.Length)
        {
            var bodySpan = span[bodyStart..];
            if (!bodySpan.IsEmpty && !bodySpan.IsWhiteSpace())
            {
                result.Body = new string(bodySpan);

                // Check body for placeholders
                if (!needsParsing && ContainsPlaceholder(bodySpan))
                {
                    needsParsing = true;
                }
            }
        }

        NormalizeCacheDirective(result, typeName);
        result.NeedsParsing = needsParsing;
        return result;
    }

    // Caching is only safe for GET (idempotent reads). A @cache directive on any other method is
    // almost always a mistake; warn and ignore it rather than caching a mutating call. Applied after
    // the whole comment is parsed so it sees @cache whether it appeared before the request line or
    // among the headers, and after the method is known.
    private static void NormalizeCacheDirective(HttpTypeDefinition result, string? typeName)
    {
        if (!result.CacheEnabled)
        {
            return;
        }
        if (!string.Equals(result.Method, "GET", StringComparison.Ordinal))
        {
            Logger?.LogWarning("Type '{TypeName}': @cache is only supported for GET requests; ignoring it for '{Method}'", typeName, result.Method);
            result.CacheEnabled = false;
            result.CacheDuration = null;
        }
        else if (result.CacheDuration is null)
        {
            Logger?.LogWarning("Type '{TypeName}': @cache has no expiration interval; responses will be cached until the process restarts", typeName);
        }
    }

    private static ReadOnlySpan<char> TrimSpan(ReadOnlySpan<char> span)
    {
        int start = 0;
        int end = span.Length - 1;
        while (start <= end && char.IsWhiteSpace(span[start])) start++;
        while (end >= start && char.IsWhiteSpace(span[end])) end--;
        return start > end ? ReadOnlySpan<char>.Empty : span[start..(end + 1)];
    }

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static bool IsValidHttpMethod(ReadOnlySpan<char> method)
    {
        return method.Length switch
        {
            3 => method is "GET" || method is "PUT",
            4 => method is "POST",
            5 => method is "PATCH",
            6 => method is "DELETE",
            _ => false
        };
    }

    private static bool ContainsPlaceholder(ReadOnlySpan<char> span)
    {
        // Find {key} where key is identifier chars (letters, digits, underscore)
        // This avoids false positives from JSON like {"name": "value"}
        int pos = 0;
        while (pos < span.Length)
        {
            int openBrace = span[pos..].IndexOf('{');
            if (openBrace < 0) return false;

            int start = pos + openBrace + 1;
            if (start >= span.Length) return false;

            // Check if first char after { is a valid identifier start (letter or underscore)
            char firstChar = span[start];
            if (char.IsLetter(firstChar) || firstChar == '_')
            {
                // Find the closing brace and verify all chars between are identifier chars
                int i = start;
                while (i < span.Length && (char.IsLetterOrDigit(span[i]) || span[i] == '_'))
                {
                    i++;
                }
                if (i < span.Length && span[i] == '}' && i > start)
                {
                    return true;
                }
            }

            pos = start;
        }
        return false;
    }

    private static TimeSpan ParseTimeoutValue(ReadOnlySpan<char> value, string? typeName)
    {
        if (value.IsEmpty)
        {
            return DefaultTimeout;
        }

        var valueStr = new string(value);

        // If contains colon, try TimeSpan.Parse (e.g., "00:00:30")
        if (value.Contains(':'))
        {
            if (TimeSpan.TryParse(valueStr, out var ts))
            {
                return ts;
            }
            Logger?.LogError("Type '{TypeName}': Failed to parse timeout value '{Value}' as TimeSpan, using default {Default}s", typeName, valueStr, DefaultTimeout.TotalSeconds);
            return DefaultTimeout;
        }

        // If only digits, treat as seconds
        bool allDigits = true;
        foreach (var c in value)
        {
            if (!char.IsDigit(c))
            {
                allDigits = false;
                break;
            }
        }

        if (allDigits)
        {
            if (int.TryParse(valueStr, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
            Logger?.LogError("Type '{TypeName}': Failed to parse timeout value '{Value}' as seconds, using default {Default}s", typeName, valueStr, DefaultTimeout.TotalSeconds);
            return DefaultTimeout;
        }

        // Otherwise use PostgresInterval parser (e.g., "30s", "5m", "1h")
        var result = Parser.ParsePostgresInterval(valueStr);
        if (result.HasValue)
        {
            return result.Value;
        }

        Logger?.LogError("Type '{TypeName}': Failed to parse timeout value '{Value}', using default {Default}s", typeName, valueStr, DefaultTimeout.TotalSeconds);
        return DefaultTimeout;
    }

    private static bool TryParseTimeoutDirective(ReadOnlySpan<char> line, string? typeName, out TimeSpan timeout)
    {
        timeout = DefaultTimeout;
        var trimmed = TrimSpan(line);

        // Remove leading # if present
        if (!trimmed.IsEmpty && trimmed[0] == '#')
        {
            trimmed = TrimSpan(trimmed[1..]);
        }

        // Remove leading @ if present
        if (!trimmed.IsEmpty && trimmed[0] == '@')
        {
            trimmed = TrimSpan(trimmed[1..]);
        }

        // Check for "timeout" keyword (case-insensitive)
        if (!trimmed.StartsWith("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var afterKeyword = trimmed[7..]; // Skip "timeout"

        // Handle separator: space, '=', or ':'
        afterKeyword = TrimSpan(afterKeyword);
        if (afterKeyword.IsEmpty)
        {
            return false;
        }

        // Skip separator character if present
        if (afterKeyword[0] == '=' || afterKeyword[0] == ':')
        {
            afterKeyword = TrimSpan(afterKeyword[1..]);
        }

        if (afterKeyword.IsEmpty)
        {
            return false;
        }

        timeout = ParseTimeoutValue(afterKeyword, typeName);
        return true;
    }

    private static bool TryParseRetryDirective(
        ReadOnlySpan<char> line,
        string? typeName,
        out TimeSpan[]? delays,
        out HashSet<int>? statusCodes)
    {
        delays = null;
        statusCodes = null;
        var trimmed = TrimSpan(line);

        // Remove leading # if present
        if (!trimmed.IsEmpty && trimmed[0] == '#')
        {
            trimmed = TrimSpan(trimmed[1..]);
        }

        // Remove leading @ if present
        if (!trimmed.IsEmpty && trimmed[0] == '@')
        {
            trimmed = TrimSpan(trimmed[1..]);
        }

        // Check for "retry_delay" keyword (case-insensitive)
        if (!trimmed.StartsWith("retry_delay", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var afterKeyword = trimmed[11..]; // Skip "retry_delay"

        // Handle separator: space, '=', or ':'
        afterKeyword = TrimSpan(afterKeyword);
        if (afterKeyword.IsEmpty)
        {
            return false;
        }

        if (afterKeyword[0] == '=' || afterKeyword[0] == ':')
        {
            afterKeyword = TrimSpan(afterKeyword[1..]);
        }

        if (afterKeyword.IsEmpty)
        {
            return false;
        }

        // Split by " on " to separate delays from status codes
        var afterStr = new string(afterKeyword);
        var onIndex = afterStr.IndexOf(" on ", StringComparison.OrdinalIgnoreCase);

        string delaysPart;
        string? codesPart = null;

        if (onIndex >= 0)
        {
            delaysPart = afterStr[..onIndex];
            codesPart = afterStr[(onIndex + 4)..]; // Skip " on "
        }
        else
        {
            delaysPart = afterStr;
        }

        // Parse delays: comma-separated time values
        var delayParts = delaysPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (delayParts.Length == 0)
        {
            Logger?.LogError("Type '{TypeName}': retry_delay has no delay values", typeName);
            return false;
        }

        var parsedDelays = new TimeSpan[delayParts.Length];
        for (int i = 0; i < delayParts.Length; i++)
        {
            parsedDelays[i] = ParseTimeoutValue(delayParts[i].AsSpan(), typeName);
        }
        delays = parsedDelays;

        // Parse status codes if present
        if (codesPart is not null)
        {
            var codeParts = codesPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var codes = new HashSet<int>();
            foreach (var code in codeParts)
            {
                if (int.TryParse(code, out var statusCode))
                {
                    codes.Add(statusCode);
                }
                else
                {
                    Logger?.LogError("Type '{TypeName}': Failed to parse retry status code '{Code}'", typeName, code);
                }
            }
            if (codes.Count > 0)
            {
                statusCodes = codes;
            }
        }

        return true;
    }

    private static bool TryParseCacheDirective(ReadOnlySpan<char> line, string? typeName, out TimeSpan? duration)
    {
        duration = null;
        var trimmed = TrimSpan(line);

        // Remove leading # if present
        if (!trimmed.IsEmpty && trimmed[0] == '#')
        {
            trimmed = TrimSpan(trimmed[1..]);
        }

        // Remove leading @ if present
        if (!trimmed.IsEmpty && trimmed[0] == '@')
        {
            trimmed = TrimSpan(trimmed[1..]);
        }

        // Check for "cache" keyword (case-insensitive). Must be the whole token, so "cache_profile"
        // or other future "cache*" directives are not swallowed here.
        if (!trimmed.StartsWith("cache", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var afterKeyword = trimmed[5..]; // Skip "cache"

        // Bare "@cache" (no value) → enabled with no expiration.
        if (afterKeyword.IsEmpty)
        {
            return true;
        }

        // The next char must be a separator (space, '=', ':'); otherwise this is a different keyword.
        char sep = afterKeyword[0];
        if (sep != ' ' && sep != '\t' && sep != '=' && sep != ':')
        {
            return false;
        }

        if (sep == '=' || sep == ':')
        {
            afterKeyword = afterKeyword[1..];
        }
        afterKeyword = TrimSpan(afterKeyword);

        // "@cache" followed only by separator → enabled with no expiration.
        if (afterKeyword.IsEmpty)
        {
            return true;
        }

        duration = ParseTimeoutValue(afterKeyword, typeName);
        return true;
    }
}