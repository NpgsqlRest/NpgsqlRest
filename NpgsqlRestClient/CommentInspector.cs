using System.Text.Json;
using NpgsqlRest;
using NpgsqlRest.Defaults;
using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestClient;

/// <summary>
/// <c>npgsqlrest --parse</c>: how NpgsqlRest reads the comment of a routine or the comments of a SQL file, line by
/// line, without a database. It runs the parsers the server runs, so an editor does not have to imitate them:
/// which lines are annotations (and whose), which are text, which are written as annotations and read by nothing.
/// What depends on the routine itself is not here: parameters, tags and types need the database
/// (<c>--inspect</c> gives those with it).
/// </summary>
public static class CommentInspector
{
    public sealed record Line(int Number, string Text, string Kind, string? Name, string? Owner, string? Form);

    public sealed class Result
    {
        public required string Source { get; init; }
        public string? File { get; init; }
        public bool RequireAtPrefix { get; init; }
        public required string CommentsMode { get; init; }
        /// <summary>Would this text become an endpoint, as far as the text alone decides it.</summary>
        public bool Endpoint { get; init; }
        public string? Reason { get; init; }
        public string? Message { get; init; }
        public int? Statements { get; init; }
        public string[] Errors { get; init; } = [];
        public List<Line> Lines { get; } = [];
    }

    private static readonly Lock Sync = new();

    /// <summary>Reads the comment of a function, procedure, table or view (the text of COMMENT ON ... IS).</summary>
    public static Result ParseComment(string comment, bool requireAtPrefix, CommentsMode commentsMode)
    {
        var lines = new List<(int Number, string Text)>();
        var number = 0;
        foreach (var raw in comment.Split('\n'))
        {
            number++;
            var text = raw.Trim();
            if (text.Length > 0)
            {
                lines.Add((number, text));
            }
        }
        return Classify("comment", null, comment, lines, new HashSet<string>(), requireAtPrefix, commentsMode, null, [], true);
    }

    /// <summary>Reads a SQL file the way the SQL file source does: its comments, and the lines the source reads itself.</summary>
    public static Result ParseSqlFile(string content, string? file, bool requireAtPrefix, CommentsMode commentsMode, CommentScope scope = CommentScope.All)
    {
        var parsed = SqlFileParser.Parse(content, scope, requireAtPrefix);

        // Where each comment line is in the file: the parser gives the comment lines in file order, so each one is
        // looked for after the one before it.
        var lines = new List<(int Number, string Text)>();
        var cursor = 0;
        foreach (var raw in parsed.Comment.Split('\n'))
        {
            var text = raw.Trim();
            if (text.Length == 0)
            {
                continue;
            }
            var index = content.IndexOf(text, cursor, StringComparison.Ordinal);
            if (index < 0)
            {
                index = content.IndexOf(text, StringComparison.Ordinal);
            }
            var lineNumber = index < 0 ? 0 : content.AsSpan(0, index).Count('\n') + 1;
            if (index >= 0)
            {
                cursor = index + text.Length;
            }
            lines.Add((lineNumber, text));
        }

        var consumed = parsed.ConsumedCommentLines;
        var remaining = string.Join('\n', lines.Where(l => consumed.Contains(l.Text) is false).Select(l => l.Text));
        return Classify("sql_file", file, remaining, lines, consumed, requireAtPrefix, commentsMode,
            parsed.Statements.Count, [.. parsed.Errors], parsed.Statements.Count > 0 && parsed.Errors.Count == 0);
    }

    private static Result Classify(
        string source, string? file, string comment, List<(int Number, string Text)> lines, IReadOnlySet<string> consumedBySource,
        bool requireAtPrefix, CommentsMode commentsMode, int? statements, string[] errors, bool sourceOk)
    {
        RoutineEndpoint endpoint;
        RoutineEndpoint? parsed;
        (string Code, string Message)? reason;
        // The comment parser reads the static options; this command has no server around it, so it supplies them
        // for the time of the call.
        lock (Sync)
        {
            var previous = NpgsqlRestOptions.Options;
            NpgsqlRestOptions.Options = new NpgsqlRestOptions
            {
                RequireAtPrefix = requireAtPrefix,
                CommentsMode = commentsMode,
            };
            try
            {
                var routine = new Routine
                {
                    Type = source == "sql_file" ? RoutineType.SqlFile : RoutineType.Function,
                    Schema = "public",
                    Name = file is null ? "routine" : Path.GetFileNameWithoutExtension(file),
                    Comment = comment.Length == 0 ? null : comment,
                    IsStrict = false,
                    CrudType = CrudType.Select,
                    ReturnsRecordType = false,
                    ReturnsSet = false,
                    ColumnCount = 0,
                    OriginalColumnNames = [],
                    ColumnNames = [],
                    JsonColumnNames = [],
                    ColumnsTypeDescriptor = [],
                    ReturnsUnnamedSet = false,
                    IsVoid = false,
                    ParamCount = 0,
                    Parameters = [],
                    ParamsHash = [],
                    OriginalParamsHash = [],
                    Expression = "",
                    FullDefinition = "",
                    SimpleDefinition = file ?? "routine",
                    FormatUrlPattern = null,
                    Tags = null,
                    EndpointHandler = null,
                    Metadata = null,
                };
                endpoint = DefaultEndpoint.CreateUnparsed(routine);
                parsed = DefaultCommentParser.Parse(routine, endpoint);
                reason = DefaultCommentParser.NotExposedReason;
            }
            finally
            {
                NpgsqlRestOptions.Options = previous;
            }
        }

        var annotations = new HashSet<string>(endpoint.AnnotationLines ?? [], StringComparer.Ordinal);
        var text = new HashSet<string>(endpoint.UnhandledCommentLines ?? [], StringComparer.Ordinal);
        var unknown = new HashSet<string>(endpoint.UnknownAnnotationLines ?? [], StringComparer.Ordinal);
        var registry = new Dictionary<string, CommentAnnotation>(StringComparer.OrdinalIgnoreCase);
        foreach (var annotation in CommentAnnotations.All)
        {
            registry.TryAdd(annotation.Name, annotation);
            foreach (var alias in annotation.Aliases)
            {
                registry.TryAdd(alias, annotation);
            }
        }

        var result = new Result
        {
            Source = source,
            File = file,
            RequireAtPrefix = requireAtPrefix,
            CommentsMode = commentsMode.ToString(),
            Endpoint = sourceOk && parsed is not null,
            Reason = sourceOk is false ? (errors.Length > 0 ? "sql_error" : "empty_file") : parsed is null ? reason?.Code : null,
            Message = sourceOk is false ? (errors.Length > 0 ? errors[0] : "The file has no SQL statement.") : parsed is null ? reason?.Message : null,
            Statements = statements,
            Errors = errors,
        };

        foreach (var (number, line) in lines)
        {
            var hasAt = line[0] == '@';
            var body = hasAt ? line[1..] : line;
            var wordEnd = body.AsSpan().IndexOfAny([' ', '\t', ',', '=', ':']);
            var first = (wordEnd < 0 ? body : body[..wordEnd]).ToLowerInvariant();
            registry.TryGetValue(first, out var known);

            if (consumedBySource.Contains(line))
            {
                result.Lines.Add(new Line(number, line, "annotation", known?.Name ?? first, known?.Owner ?? "sqlfilesource", "keyword"));
            }
            else if (unknown.Contains(line))
            {
                result.Lines.Add(new Line(number, line, "unknown_annotation", first, null, null));
            }
            else if (annotations.Contains(line))
            {
                // key = value, a response header (Name: value), or a keyword
                var isKeyValue = IsSeparated(body, '=');
                var isHeader = isKeyValue is false && known is null && IsSeparated(body, ':');
                result.Lines.Add(isHeader
                    ? new Line(number, line, "annotation", body[..body.IndexOf(':')].Trim(), "core", "header")
                    : new Line(number, line, "annotation", known?.Name ?? first, known?.Owner ?? (isKeyValue ? "custom" : "core"), isKeyValue ? "key_value" : "keyword"));
            }
            else if (text.Contains(line))
            {
                // A plugin's annotation is read by the plugin when it is enabled; without it the line is text.
                // It is reported as that plugin's annotation, which is what it is to whoever wrote it.
                if (known is not null && known.Owner != "core" && (hasAt || requireAtPrefix is false) && known.Form != AnnotationForm.KeyValue)
                {
                    result.Lines.Add(new Line(number, line, "annotation", known.Name, known.Owner, "keyword"));
                }
                else
                {
                    result.Lines.Add(new Line(number, line, "text", null, null, null));
                }
            }
            else
            {
                // written with the @ and known to the reference: an annotation of a plugin or handler
                result.Lines.Add(new Line(number, line, "annotation", known?.Name ?? first, known?.Owner, "keyword"));
            }
        }
        return result;
    }

    private static bool IsSeparated(string body, char separator)
    {
        var index = body.IndexOf(separator);
        if (index <= 0)
        {
            return false;
        }
        foreach (var c in body.AsSpan(0, index).Trim())
        {
            if (char.IsLetterOrDigit(c) is false && c != '-' && c != '_')
            {
                return false;
            }
        }
        return true;
    }

    public static void WriteJson(Utf8JsonWriter writer, Result result)
    {
        writer.WriteStartObject();
        writer.WriteString("source", result.Source);
        if (result.File is not null) writer.WriteString("file", result.File);
        writer.WriteBoolean("requireAtPrefix", result.RequireAtPrefix);
        writer.WriteString("commentsMode", result.CommentsMode);
        writer.WriteBoolean("endpoint", result.Endpoint);
        if (result.Reason is not null) writer.WriteString("reason", result.Reason);
        if (result.Message is not null) writer.WriteString("message", result.Message);
        if (result.Statements is not null) writer.WriteNumber("statements", result.Statements.Value);
        if (result.Errors.Length > 0)
        {
            writer.WriteStartArray("errors");
            foreach (var error in result.Errors) writer.WriteStringValue(error);
            writer.WriteEndArray();
        }
        writer.WriteStartArray("lines");
        foreach (var line in result.Lines)
        {
            writer.WriteStartObject();
            writer.WriteNumber("line", line.Number);
            writer.WriteString("text", line.Text);
            writer.WriteString("kind", line.Kind);
            if (line.Name is not null) writer.WriteString("name", line.Name);
            if (line.Owner is not null) writer.WriteString("owner", line.Owner);
            if (line.Form is not null) writer.WriteString("form", line.Form);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
