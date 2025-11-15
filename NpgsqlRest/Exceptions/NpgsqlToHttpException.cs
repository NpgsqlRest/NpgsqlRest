using Npgsql;

namespace NpgsqlRest;

public class NpgsqlToHttpException(ErrorCodeMappingOptions entry, NpgsqlException innerException)
    : Exception(
        entry.Title ?? (innerException is PostgresException exception ? exception.MessageText : innerException.Message), innerException)
{
    public ErrorCodeMappingOptions Mapping { get; } = entry;
    public string? SqlState { get; } = innerException.SqlState;
}
