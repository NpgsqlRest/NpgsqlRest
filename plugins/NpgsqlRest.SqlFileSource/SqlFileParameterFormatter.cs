namespace NpgsqlRest.SqlFileSource;

/// <summary>
/// No-op parameter formatter for SQL file endpoints.
/// SQL files already contain complete command text with $N placeholders in routine.Expression.
/// IsFormattable = false means the rendering uses Expression directly.
/// Interface defaults (returning null) are safe — StringBuilder.Append(null) is a no-op.
/// Single static instance shared by all SQL file endpoints.
/// </summary>
public class SqlFileParameterFormatter : IRoutineSourceParameterFormatter
{
    public static readonly SqlFileParameterFormatter Instance = new();

    public bool IsFormattable => false;
}
