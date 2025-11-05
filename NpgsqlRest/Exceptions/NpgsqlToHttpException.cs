using Npgsql;

namespace NpgsqlRest;

public class NpgsqlToHttpException(ErrorCodeMappingOptions entry, PostgresException innerException)
    : Exception(entry.Title ?? innerException.MessageText, innerException)
{
    public ErrorCodeMappingOptions Mapping { get; } = entry;
    public string? SqlState { get; } = innerException.SqlState;
}
