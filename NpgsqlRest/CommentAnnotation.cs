using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NpgsqlRest;

/// <summary>Where a comment annotation is read.</summary>
[Flags]
public enum AnnotationContext
{
    None = 0,
    /// <summary>The comment of a function, procedure, table or view (COMMENT ON ...).</summary>
    Routine = 1,
    /// <summary>A comment anywhere in a SQL file endpoint; applies to the whole file.</summary>
    SqlFile = 2,
    /// <summary>A comment in a SQL file endpoint that applies to one statement by its position.</summary>
    SqlFileStatement = 4,
    /// <summary>A comment in a SQL test file (npgsqlrest --test).</summary>
    TestFile = 8,
    /// <summary>The comment of a composite type that defines an HTTP Custom Type.</summary>
    TypeComment = 16,

    /// <summary>Every endpoint comment: a routine's and a SQL file's.</summary>
    Endpoint = Routine | SqlFile
}

/// <summary>How an annotation is written on its comment line.</summary>
public enum AnnotationForm
{
    /// <summary>A keyword that opens the line, followed by its value: <c>authorize admin</c>.</summary>
    Keyword,
    /// <summary>A custom parameter: <c>key = value</c>.</summary>
    KeyValue,
    /// <summary>Either of the two: <c>timeout 30s</c> or <c>timeout = 30s</c>.</summary>
    KeywordOrKeyValue,
    /// <summary>A line with a shape of its own, described by its syntax (a response header, a request line).</summary>
    Line
}

/// <summary>What kind of value an annotation takes.</summary>
public enum AnnotationValueKind
{
    /// <summary>No value: the annotation is a switch.</summary>
    None,
    Boolean,
    Integer,
    /// <summary>Free text to the end of the line.</summary>
    Text,
    /// <summary>One of <see cref="CommentAnnotation.Values"/>.</summary>
    Enum,
    /// <summary>A PostgreSQL interval: <c>30s</c>, <c>5 minutes</c>.</summary>
    Interval,
    /// <summary>SQL text.</summary>
    Sql,
    /// <summary>A list of values separated by commas or spaces.</summary>
    List,
    /// <summary>A URL path or a file system path.</summary>
    Path,
    /// <summary>A name defined elsewhere: a parameter, a column, a configured policy or connection.</summary>
    Name
}

/// <summary>
/// One comment annotation, described as data: what it is called, how it is written, where it is read and what
/// value it takes. The descriptions of all annotations make up the reference that <c>npgsqlrest --annotations</c>
/// prints, which editors and other tools use for completion, validation and help. They are also how NpgsqlRest
/// tells an annotation it does not know from one a plugin reads.
/// </summary>
public sealed class CommentAnnotation
{
    /// <summary>The canonical name, lower case, without the optional <c>@</c> prefix.</summary>
    public required string Name { get; init; }

    /// <summary>Every accepted spelling, the canonical name included.</summary>
    public string[] Aliases { get; init; } = [];

    /// <summary>The line as it is written: <c>authorize [role1, role2, ...]</c>.</summary>
    public required string Syntax { get; init; }

    /// <summary>What it does, written to be read on its own.</summary>
    public required string Description { get; init; }

    /// <summary>Who reads it: <c>core</c>, or a plugin or handler (<c>tsclient</c>, <c>upload:csv</c>).</summary>
    public string Owner { get; init; } = "core";

    public AnnotationForm Form { get; init; } = AnnotationForm.Keyword;

    public AnnotationContext Contexts { get; init; } = AnnotationContext.Endpoint;

    public AnnotationValueKind ValueKind { get; init; } = AnnotationValueKind.Text;

    /// <summary>The accepted values, when <see cref="ValueKind"/> is <see cref="AnnotationValueKind.Enum"/>.</summary>
    public string[]? Values { get; init; }

    /// <summary>What applies when the annotation is absent, where that can be said in a word.</summary>
    public string? Default { get; init; }

    /// <summary>One line as it would be written in a comment.</summary>
    public string? Example { get; init; }

    /// <summary>
    /// A regular expression for names that follow a pattern rather than a list (<c>^result[0-9]+$</c>). Matched
    /// case-insensitively against the name without its <c>@</c> prefix.
    /// </summary>
    public string? Pattern { get; init; }

    internal Regex? PatternRegex => _patternRegex ??= Pattern is null
        ? null
        : new Regex(Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private Regex? _patternRegex;

    public JsonObject ToJson()
    {
        var aliases = new JsonArray();
        foreach (var alias in Aliases)
        {
            aliases.Add((JsonNode?)alias);
        }
        var contexts = new JsonArray();
        if (Contexts.HasFlag(AnnotationContext.Routine)) contexts.Add((JsonNode?)"routine");
        if (Contexts.HasFlag(AnnotationContext.SqlFile)) contexts.Add((JsonNode?)"sql_file");
        if (Contexts.HasFlag(AnnotationContext.SqlFileStatement)) contexts.Add((JsonNode?)"sql_file_statement");
        if (Contexts.HasFlag(AnnotationContext.TestFile)) contexts.Add((JsonNode?)"test_file");
        if (Contexts.HasFlag(AnnotationContext.TypeComment)) contexts.Add((JsonNode?)"type_comment");

        var result = new JsonObject
        {
            ["name"] = Name,
            ["aliases"] = aliases,
            ["syntax"] = Syntax,
            ["description"] = Description,
            ["owner"] = Owner,
            ["form"] = Form switch
            {
                AnnotationForm.Keyword => "keyword",
                AnnotationForm.KeyValue => "key_value",
                AnnotationForm.KeywordOrKeyValue => "keyword_or_key_value",
                _ => "line"
            },
            ["contexts"] = contexts,
            ["valueKind"] = ValueKind switch
            {
                AnnotationValueKind.None => "none",
                AnnotationValueKind.Boolean => "boolean",
                AnnotationValueKind.Integer => "integer",
                AnnotationValueKind.Enum => "enum",
                AnnotationValueKind.Interval => "interval",
                AnnotationValueKind.Sql => "sql",
                AnnotationValueKind.List => "list",
                AnnotationValueKind.Path => "path",
                AnnotationValueKind.Name => "name",
                _ => "text"
            }
        };
        if (Values is not null)
        {
            var values = new JsonArray();
            foreach (var value in Values)
            {
                values.Add((JsonNode?)value);
            }
            result["values"] = values;
        }
        if (Default is not null) result["default"] = Default;
        if (Example is not null) result["example"] = Example;
        if (Pattern is not null) result["pattern"] = Pattern;
        return result;
    }
}

/// <summary>
/// The annotations NpgsqlRest and its plugins read. Core registers its own; a plugin registers what it reads
/// (<see cref="IEndpointCreateHandler.Annotations"/> is registered automatically when the plugin is used, and a
/// host may call <see cref="Register"/> for plugins it ships but has not enabled).
/// </summary>
public static class CommentAnnotations
{
    private static readonly Lock Sync = new();
    private static readonly List<CommentAnnotation> List = [];
    private static readonly HashSet<string> Registered = new(StringComparer.OrdinalIgnoreCase);
    // names and aliases read in endpoint comments (a routine's, a SQL file's)
    private static HashSet<string> _endpointNames = new(StringComparer.OrdinalIgnoreCase);
    // the ones among them that are written as key = value
    private static HashSet<string> _endpointKeys = new(StringComparer.OrdinalIgnoreCase);
    private static CommentAnnotation[] _endpointPatterns = [];
    private static bool _coreRegistered;

    /// <summary>Adds annotations to the reference. One with the owner and name of an earlier one is left out.</summary>
    public static void Register(IEnumerable<CommentAnnotation> annotations)
    {
        lock (Sync)
        {
            EnsureCore();
            Add(annotations);
        }
    }

    /// <summary>Every registered annotation, core's first, then in the order they were registered.</summary>
    public static CommentAnnotation[] All
    {
        get
        {
            lock (Sync)
            {
                EnsureCore();
                return [.. List];
            }
        }
    }

    /// <summary>
    /// True when <paramref name="name"/> (with or without its <c>@</c> prefix) is an annotation or a custom
    /// parameter that something reads in an endpoint comment.
    /// </summary>
    public static bool IsKnown(string name)
    {
        if (name.Length > 0 && name[0] == '@')
        {
            name = name[1..];
        }
        HashSet<string> names;
        CommentAnnotation[] patterns;
        lock (Sync)
        {
            EnsureCore();
            names = _endpointNames;
            patterns = _endpointPatterns;
        }
        if (names.Contains(name))
        {
            return true;
        }
        foreach (var annotation in patterns)
        {
            if (annotation.PatternRegex!.IsMatch(name))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="key"/> (with or without its <c>@</c> prefix) is something read from a
    /// <c>key = value</c> line of an endpoint comment. A name that is only ever a keyword (<c>authorize</c>) is
    /// not: written as <c>authorize = admin</c> it is stored as a custom parameter that nothing reads.
    /// </summary>
    public static bool IsKnownCustomParameter(string key)
    {
        if (key.Length > 0 && key[0] == '@')
        {
            key = key[1..];
        }
        lock (Sync)
        {
            EnsureCore();
            return _endpointKeys.Contains(key);
        }
    }

    /// <summary>The reference as JSON: what <c>npgsqlrest --annotations</c> prints.</summary>
    public static JsonArray ToJson()
    {
        var result = new JsonArray();
        foreach (var annotation in All)
        {
            result.Add((JsonNode?)annotation.ToJson());
        }
        return result;
    }

    private static void EnsureCore()
    {
        if (_coreRegistered)
        {
            return;
        }
        _coreRegistered = true;
        Add(Defaults.DefaultCommentParser.CoreAnnotations());
    }

    private static void Add(IEnumerable<CommentAnnotation> annotations)
    {
        var changed = false;
        foreach (var annotation in annotations)
        {
            if (Registered.Add(string.Concat(annotation.Owner, "\n", annotation.Name)) is false)
            {
                continue;
            }
            List.Add(annotation);
            changed = true;
        }
        if (changed is false)
        {
            return;
        }
        // copy on write: IsKnown reads these without holding the lock for long
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new List<CommentAnnotation>();
        foreach (var annotation in List)
        {
            if ((annotation.Contexts & (AnnotationContext.Endpoint | AnnotationContext.SqlFileStatement)) == 0)
            {
                continue;
            }
            var isKey = annotation.Form is AnnotationForm.KeyValue or AnnotationForm.KeywordOrKeyValue;
            names.Add(annotation.Name);
            if (isKey) keys.Add(annotation.Name);
            foreach (var alias in annotation.Aliases)
            {
                names.Add(alias);
                if (isKey) keys.Add(alias);
            }
            if (annotation.Pattern is not null)
            {
                patterns.Add(annotation);
            }
        }
        _endpointNames = names;
        _endpointKeys = keys;
        _endpointPatterns = [.. patterns];
    }
}
