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
    /// <summary>Log error, skip the file, continue.</summary>
    Skip,
    /// <summary>Log error and exit the process — default.</summary>
    Exit
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
    /// Default is OnlyWithHttpTag — SQL files must contain an explicit HTTP annotation
    /// (e.g., "-- HTTP GET") to become endpoints. This prevents accidental exposure of
    /// utility scripts or migration files that match the glob pattern.
    /// </summary>
    public CommentsMode CommentsMode { get; set; } = CommentsMode.OnlyWithHttpTag;

    /// <summary>
    /// Which comments in the file to parse as annotations.
    /// </summary>
    public CommentScope CommentScope { get; set; } = CommentScope.All;

    /// <summary>
    /// Behavior when a file fails to parse or describe.
    /// </summary>
    public ParseErrorMode ErrorMode { get; set; } = ParseErrorMode.Exit;

    /// <summary>
    /// When true, queries returning a single column produce a flat JSON array of values
    /// (e.g., ["a", "b", "c"]) instead of an array of objects (e.g., [{"col": "a"}, {"col": "b"}]).
    /// This matches the behavior of PostgreSQL functions returning setof single values.
    /// Default is true.
    /// </summary>
    public bool UnnamedSingleColumnSet { get; set; } = true;

    /// <summary>
    /// Prefix for result keys in multi-command JSON responses.
    /// Default keys are "result1", "result2", etc.
    /// Override per-result with positional @result annotation in the SQL file.
    /// </summary>
    public string ResultPrefix { get; set; } = "result";

    /// <summary>
    /// When true, non-query commands in multi-command files are still executed but excluded
    /// from the JSON response result keys. This includes transaction control (BEGIN, COMMIT,
    /// ROLLBACK), session commands (SET, RESET), DO blocks, and other utility statements
    /// (DISCARD, LOCK, LISTEN, NOTIFY, DEALLOCATE).
    /// Default is true.
    /// </summary>
    public bool SkipNonQueryCommands { get; set; } = true;
}
