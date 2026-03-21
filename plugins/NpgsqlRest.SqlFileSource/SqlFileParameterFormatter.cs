using Npgsql;

namespace NpgsqlRest.SqlFileSource;

/// <summary>
/// Parameter formatter for SQL file endpoints.
/// SQL files contain complete command text with $N placeholders — the formatter
/// returns the full SQL as the command, with parameters bound positionally.
/// </summary>
public class SqlFileParameterFormatter(string sql) : IRoutineSourceParameterFormatter
{
    public bool IsFormattable { get; } = true;

    public string? FormatCommand(Routine routine, NpgsqlParameterCollection parameters)
    {
        return sql;
    }

    public string? AppendEmpty() => sql;
}
