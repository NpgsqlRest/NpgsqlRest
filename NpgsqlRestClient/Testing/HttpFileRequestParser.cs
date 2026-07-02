namespace NpgsqlRestClient.Testing;

/// <summary>
/// Parses a single HTTP request out of a block-comment body, using a single-request subset of the
/// Microsoft .http syntax plus the test directives <c># @claim</c> and <c># @response</c>.
/// Returns null when the block is not an HTTP request (ordinary SQL comment).
/// </summary>
public static class HttpFileRequestParser
{
    private static readonly string[] Methods = ["GET", "PUT", "POST", "DELETE"];

    /// <summary>
    /// Try to parse <paramref name="commentBody"/> (the text between /* and */) as an HTTP request.
    /// Returns null if the block's first non-blank/non-comment line is not a request line.
    /// </summary>
    public static HttpStep? TryParse(string commentBody, int lineNumber)
    {
        var lines = commentBody.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // Find the request line: first line that is not blank and not a #/// comment line.
        int idx = 0;
        string? requestLine = null;
        for (; idx < lines.Length; idx++)
        {
            var t = lines[idx].Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith('#') || t.StartsWith("//")) continue;
            requestLine = t;
            break;
        }
        if (requestLine is null) return null;

        if (!TryParseRequestLine(requestLine, out var method, out var path)) return null;

        idx++; // consume the request line

        var headers = new List<(string, string)>();
        var claims = new List<(string, string)>();
        string? responseTable = null;
        int bodyStart = -1;

        for (; idx < lines.Length; idx++)
        {
            var t = lines[idx].Trim();
            if (t.Length == 0)
            {
                bodyStart = idx + 1; // body begins after the blank line
                break;
            }

            string? directive = null;
            if (t.StartsWith('#')) directive = t[1..].TrimStart();
            else if (t.StartsWith("//")) directive = t[2..].TrimStart();

            if (directive is not null)
            {
                if (directive.StartsWith('@'))
                {
                    ParseDirective(directive[1..].Trim(), claims, ref responseTable);
                }
                // plain comment line → ignore
                continue;
            }

            // Header: Name: Value
            int colon = t.IndexOf(':');
            if (colon > 0)
            {
                headers.Add((t[..colon].Trim(), t[(colon + 1)..].Trim()));
            }
            // otherwise: stray line in the header region → ignore
        }

        string? body = null;
        if (bodyStart >= 0 && bodyStart < lines.Length)
        {
            var bodyText = string.Join('\n', lines[bodyStart..]).Trim();
            if (bodyText.Length > 0) body = bodyText;
        }

        return new HttpStep(method, path, headers, claims, body, responseTable, lineNumber);
    }

    // [HTTP] METHOD /path [HTTP/x]. Method required (GET|PUT|POST|DELETE); path must start with '/';
    // an optional trailing HTTP-version token is allowed but ignored; anything else → not a request.
    private static bool TryParseRequestLine(string line, out string method, out string path)
    {
        method = "";
        path = "";
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        int ti = 0;
        if (string.Equals(tokens[ti], "HTTP", StringComparison.OrdinalIgnoreCase))
        {
            ti++; // optional leading HTTP keyword
        }
        if (ti >= tokens.Length) return false;

        var m = tokens[ti].ToUpperInvariant();
        if (Array.IndexOf(Methods, m) < 0) return false;
        ti++;

        if (ti >= tokens.Length) return false; // method but no path
        var p = tokens[ti];
        if (!p.StartsWith('/')) return false;  // reject prose like "GET the data"
        ti++;

        // At most one trailing token, which must be an HTTP-version; otherwise it's prose, not a request.
        if (ti < tokens.Length)
        {
            if (!tokens[ti].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase)) return false;
            ti++;
            if (ti < tokens.Length) return false;
        }

        method = m;
        path = p;
        return true;
    }

    private static void ParseDirective(
        string directive,
        List<(string, string)> claims,
        ref string? responseTable)
    {
        int sp = directive.IndexOfAny([' ', '\t']);
        var keyword = sp < 0 ? directive : directive[..sp];
        var rest = sp < 0 ? "" : directive[(sp + 1)..].Trim();

        if (string.Equals(keyword, "claim", StringComparison.OrdinalIgnoreCase))
        {
            int eq = rest.IndexOf('=');
            if (eq > 0)
            {
                claims.Add((rest[..eq].Trim(), rest[(eq + 1)..].Trim()));
            }
        }
        else if (string.Equals(keyword, "response", StringComparison.OrdinalIgnoreCase))
        {
            if (rest.Length > 0) responseTable = rest;
        }
    }
}
