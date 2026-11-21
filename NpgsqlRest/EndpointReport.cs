namespace NpgsqlRest;

/// <summary>
/// A routine, table, view or SQL file that NpgsqlRest looked at and did not turn into an endpoint, with the reason.
/// </summary>
public sealed class NotExposedEntry
{
    /// <summary>What it is: <c>function</c>, <c>procedure</c>, <c>table</c>, <c>view</c>, <c>sql_file</c>.</summary>
    public required string Kind { get; init; }
    public string? Schema { get; init; }
    public string? Name { get; init; }
    /// <summary>The routine's signature, as in <see cref="Routine.SimpleDefinition"/>.</summary>
    public string? Definition { get; init; }
    /// <summary>The file of a SQL file endpoint.</summary>
    public string? SourceFile { get; init; }
    /// <summary>
    /// Why, as a stable code: <c>no_comment</c>, <c>no_http_annotation</c>, <c>disabled</c>, <c>tag_mismatch</c>,
    /// <c>excluded_by_filter</c>, <c>sql_error</c>, <c>empty_file</c>, <c>rejected_by_callback</c>.
    /// </summary>
    public required string Reason { get; init; }
    /// <summary>The reason in a sentence, with what to change.</summary>
    public required string Message { get; init; }
    /// <summary>Warnings and errors logged about it while it was read.</summary>
    public string[]? Diagnostics { get; init; }
}

/// <summary>A file that a generator wrote for an endpoint (a client module, an HTTP file, an OpenAPI document).</summary>
public sealed class GeneratedFileEntry
{
    /// <summary>Who wrote it: <c>tsclient</c>, <c>dartclient</c>, <c>httpfiles</c>, <c>openapi</c>, <c>mcp</c>.</summary>
    public required string Generator { get; init; }
    /// <summary>The full path of the file.</summary>
    public required string File { get; init; }
    /// <summary>What the endpoint is called in that file (a function name, an operation id), when it has a name there.</summary>
    public string? Symbol { get; init; }
    /// <summary>The line (from 1) where it begins in the file as written, when the generator knows it.</summary>
    public int? Line { get; init; }
    /// <summary>What kind of file it is for the generator: <c>client</c>, <c>types</c>, <c>hooks</c>, <c>requests</c>, <c>document</c>.</summary>
    public string? Role { get; init; }
}

/// <summary>
/// What the last build of the endpoints left behind for tools: everything that was looked at and is not an
/// endpoint (<see cref="NotExposed"/>), and the files generated for each endpoint (<see cref="GeneratedFor"/>).
/// <c>npgsqlrest --inspect</c> prints it. It is filled by every build and replaced by the next one.
/// </summary>
public static class EndpointReport
{
    private static readonly Lock Sync = new();
    private static List<NotExposedEntry> _notExposed = [];
    private static Dictionary<RoutineEndpoint, List<GeneratedFileEntry>> _generated = new(ReferenceEqualityComparer.Instance);
    private static List<GeneratedFileEntry> _generatedFiles = [];

    /// <summary>
    /// Also look for the routines that the schema, name and language filters left out, which takes one more query
    /// per routine source. Off for a normal start; <c>npgsqlrest --inspect</c> turns it on.
    /// </summary>
    public static bool IncludeFilteredOut { get; set; }

    /// <summary>Everything that was looked at and is not an endpoint, in the order it was found.</summary>
    public static NotExposedEntry[] NotExposed
    {
        get { lock (Sync) { return [.. _notExposed]; } }
    }

    /// <summary>The files generated for an endpoint; empty when there are none.</summary>
    public static GeneratedFileEntry[] GeneratedFor(RoutineEndpoint endpoint)
    {
        lock (Sync)
        {
            return _generated.TryGetValue(endpoint, out var list) ? [.. list] : [];
        }
    }

    /// <summary>Every generated file that is not about one endpoint (a shared types file), and all the others, once each.</summary>
    public static GeneratedFileEntry[] GeneratedFiles
    {
        get { lock (Sync) { return [.. _generatedFiles]; } }
    }

    /// <summary>Records something that did not become an endpoint. Sources and plugins call this for what they leave out.</summary>
    public static void AddNotExposed(NotExposedEntry entry)
    {
        lock (Sync) { _notExposed.Add(entry); }
    }

    /// <summary>Records a file a generator wrote for an endpoint (or, with no endpoint, a file that belongs to all of them).</summary>
    public static void AddGenerated(RoutineEndpoint? endpoint, GeneratedFileEntry entry)
    {
        lock (Sync)
        {
            if (endpoint is not null)
            {
                if (_generated.TryGetValue(endpoint, out var list) is false)
                {
                    _generated[endpoint] = list = [];
                }
                list.Add(entry);
            }
            if (_generatedFiles.Exists(f => f.Generator == entry.Generator && f.File == entry.File && f.Role == entry.Role) is false)
            {
                _generatedFiles.Add(new GeneratedFileEntry { Generator = entry.Generator, File = entry.File, Role = entry.Role });
            }
        }
    }

    /// <summary>
    /// For a generator: records, for each endpoint, where it is in a file that was just written. The line of an
    /// endpoint is the line of the first of its <c>markers</c> found in <paramref name="text"/> (the text as
    /// written); an endpoint none of whose markers is found is recorded without a line.
    /// </summary>
    public static void AddGenerated(
        string generator, string file, string role, string text,
        IEnumerable<(RoutineEndpoint Endpoint, string Symbol, string[] Markers)> items)
    {
        file = Path.GetFullPath(file);
        var any = false;
        foreach (var (endpoint, symbol, markers) in items)
        {
            any = true;
            int? line = null;
            foreach (var marker in markers)
            {
                var index = IndexOfWhole(text, marker);
                if (index >= 0)
                {
                    line = text.AsSpan(0, index).Count('\n') + 1;
                    break;
                }
            }
            AddGenerated(endpoint, new GeneratedFileEntry { Generator = generator, File = file, Role = role, Symbol = symbol, Line = line });
        }
        if (any is false)
        {
            AddGenerated(null, new GeneratedFileEntry { Generator = generator, File = file, Role = role });
        }
    }

    // The marker, not followed by a character that would make it part of a longer name or path
    // ("GET {{host}}/api/a" is not found in "GET {{host}}/api/ab").
    private static int IndexOfWhole(string text, string marker)
    {
        var from = 0;
        while (true)
        {
            var index = text.IndexOf(marker, from, StringComparison.Ordinal);
            if (index < 0)
            {
                return -1;
            }
            var end = index + marker.Length;
            if (end >= text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] is '_' or '-' or '/' or '{'))
            {
                return index;
            }
            from = index + 1;
        }
    }

    /// <summary>
    /// Starts keeping the warnings and errors logged on this thread through NpgsqlRest's logger, so that a source
    /// can attach them to what it reports (<see cref="EndCapture"/> returns them).
    /// </summary>
    public static void BeginCapture() => EndpointDiagnosticsLogger.Begin();

    /// <summary>Stops, and returns what was logged since <see cref="BeginCapture"/> (null for nothing).</summary>
    public static string[]? EndCapture() => EndpointDiagnosticsLogger.End();

    internal static void Reset()
    {
        lock (Sync)
        {
            _notExposed = [];
            _generated = new(ReferenceEqualityComparer.Instance);
            _generatedFiles = [];
        }
    }

    internal static string KindOf(Routine routine) => routine.Type switch
    {
        RoutineType.Function => "function",
        RoutineType.Procedure => "procedure",
        RoutineType.Table => "table",
        RoutineType.View => "view",
        RoutineType.SqlFile => "sql_file",
        _ => routine.Type.ToString().ToLowerInvariant()
    };
}
