namespace NpgsqlRest.SqlFileSource;

/// <summary>
/// Which comments in the SQL file to parse as annotations.
/// </summary>
public enum CommentScope
{
    /// <summary>All comments in the file — default.</summary>
    All,
    /// <summary>Only comments before the first statement.</summary>
    Header
}

/// <summary>
/// Behavior when a SQL file fails to parse or describe.
/// </summary>
public enum ParseErrorMode
{
    /// <summary>Log error, skip the file, continue — default (production-safe).</summary>
    Skip,
    /// <summary>Throw exception, halt startup (good for dev/CI).</summary>
    Throw
}

/// <summary>
/// Configuration options for the SQL file source plugin.
/// </summary>
public class SqlFileSourceOptions
{
    /// <summary>
    /// Glob pattern for SQL files, e.g. "sql/**/*.sql", "queries/*.sql".
    /// Supports * (any chars), ** (recursive, any including /), ? (single char).
    /// Empty string (default) disables the feature — no endpoints created.
    /// </summary>
    public string FilePattern { get; set; } = "";

    /// <summary>
    /// How comment annotations are processed for this source.
    /// Default is ParseAll — every SQL file becomes an endpoint, comments configure it.
    /// </summary>
    public CommentsMode CommentsMode { get; set; } = CommentsMode.ParseAll;

    /// <summary>
    /// Which comments in the file to parse as annotations.
    /// </summary>
    public CommentScope CommentScope { get; set; } = CommentScope.All;

    /// <summary>
    /// Behavior when a file fails to parse or describe.
    /// </summary>
    public ParseErrorMode ErrorMode { get; set; } = ParseErrorMode.Skip;
}
