using Npgsql;

namespace NpgsqlRest;

public class NpgsqlRestCommand : NpgsqlCommand
{
    private NpgsqlCommand NpgsqlCommandClone()
    {
#pragma warning disable CS8603 // Possible null reference return.
        return MemberwiseClone() as NpgsqlCommand;
#pragma warning restore CS8603 // Possible null reference return.
    }

    private static readonly NpgsqlRestCommand InstanceCache = new();

    public static NpgsqlCommand Create(NpgsqlConnection connection)
    {
        var result = InstanceCache.NpgsqlCommandClone();
        result.Connection = connection;
        return result;
    }
}
