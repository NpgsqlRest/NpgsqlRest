using Npgsql; 

namespace NpgsqlRest;

public class NpgsqlRestCommand : NpgsqlCommand
{
    public static NpgsqlCommand Create(NpgsqlConnection connection)
    {
        return new NpgsqlCommand { Connection = connection };
    }
}