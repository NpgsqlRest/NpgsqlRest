using System.Reflection;
using Npgsql;

// ReSharper disable once CheckNamespace
namespace NpgsqlRestTests;

public static partial class Database
{
    private const string Dbname = "npgsql_rest_test";
    private const string InitialConnectionString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
    private static readonly StringBuilder script = new();
    private static readonly object _createLock = new();
    private static volatile bool _created = false;

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

    public static string GetIinitialConnectionString() => InitialConnectionString;

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
        // Thread-safe singleton pattern - only create database once per test run
        if (_created)
        {
            var builder = new NpgsqlConnectionStringBuilder(InitialConnectionString)
            {
                Database = Dbname
            };
            return builder.ConnectionString;
        }

        lock (_createLock)
        {
            if (_created)
            {
                var builder = new NpgsqlConnectionStringBuilder(InitialConnectionString)
                {
                    Database = Dbname
                };
                return builder.ConnectionString;
            }

            DropIfExists();
            var connBuilder = new NpgsqlConnectionStringBuilder(InitialConnectionString)
            {
                Database = Dbname
            };

            using NpgsqlConnection test = new(connBuilder.ConnectionString);
            test.Open();
            using var command = test.CreateCommand();
            command.CommandText = script.ToString();
            command.ExecuteNonQuery();

            _created = true;
            return connBuilder.ConnectionString;
        }
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

    /// <summary>
    /// Creates a connection string for the test_user with minimal privileges (PoLP testing).
    /// </summary>
    public static string CreatePolpConnection()
    {
        var builder = new NpgsqlConnectionStringBuilder(InitialConnectionString)
        {
            Database = Dbname,
            Username = "test_user",
            Password = "test_pass"
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
