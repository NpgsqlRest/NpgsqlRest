using System.Reflection;
using Npgsql;

// ReSharper disable once CheckNamespace
namespace NpgsqlRestTests;

public static partial class Database
{
    private const string Dbname = "npgsql_rest_test";
    private const string InitialConnectionString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
    private static readonly StringBuilder script = new();

    static Database()
    {
        foreach (var method in typeof(Database).GetMethods(BindingFlags.Static | BindingFlags.Public))
        {
            if (method.GetParameters().Length == 0 && !string.Equals(method.Name, "Create", StringComparison.OrdinalIgnoreCase))
            {
                method.Invoke(null, []);
            }
        }
    }

    public static NpgsqlConnection CreateConnection()
    {
        var builder = new NpgsqlConnectionStringBuilder(InitialConnectionString)
        {
            Database = Dbname
        };
        return new NpgsqlConnection(builder.ConnectionString);
    }

    public static string Create()
    {
        DropIfExists();
        var builder = new NpgsqlConnectionStringBuilder(InitialConnectionString)
        {
            Database = Dbname
        };

        using NpgsqlConnection test = new(builder.ConnectionString);
        test.Open();
        using var command = test.CreateCommand();
        command.CommandText = script.ToString();
        command.ExecuteNonQuery();

        return builder.ConnectionString;
    }

    public static string CreateAdditional(string appName)
    {
        var builder = new NpgsqlConnectionStringBuilder(InitialConnectionString)
        {
            Database = Dbname,
            ApplicationName = appName
        };

        return builder.ConnectionString;
    }

    public static void DropIfExists()
    {
        using NpgsqlConnection connection = new(InitialConnectionString);
        connection.Open();
        void Exec(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        bool Any(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return true;
            }
            return false;
        }

        if (Any($"select 1 from pg_database where datname = '{Dbname}'"))
        {
            Exec($"revoke connect on database {Dbname} from public");
            Exec($"select pg_terminate_backend(pid) from pg_stat_activity where datname = '{Dbname}' and pid <> pg_backend_pid()");
            Exec($"drop database {Dbname}");
        }
        Exec($"create database {Dbname}");
        Exec($"alter database {Dbname} set timezone to 'UTC'");
    }
}
