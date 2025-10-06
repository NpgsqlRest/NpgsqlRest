using Npgsql;

namespace NpgsqlRest;

public class NpgsqlRestBatch : NpgsqlBatch
{
    public static NpgsqlBatch Create(NpgsqlConnection connection)
    {
        return new NpgsqlBatch { Connection = connection };
    }
}