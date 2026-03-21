using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("SqlFileSourceFixture")]
public class SqlFileSourceFixtureCollection : ICollectionFixture<SqlFileSourceTestFixture> { }

/// <summary>
/// Test fixture for SqlFileSource endpoint tests.
/// Creates SQL files in a temp directory and starts a web application
/// with SqlFileSource configured to scan them.
/// </summary>
public class SqlFileSourceTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _sqlDir;

    public HttpClient Client => _client;
    public string SqlDir => _sqlDir;

    public SqlFileSourceTestFixture()
    {
        var connectionString = Database.Create();

        // Create temp directory for SQL files
        _sqlDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_sql_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_sqlDir);
        var subDir = Path.Combine(_sqlDir, "sub");
        Directory.CreateDirectory(subDir);

        // Write SQL files for testing
        WriteSqlFiles(_sqlDir, subDir);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            CommentsMode = CommentsMode.ParseAll,
            RoutineSources =
            [
                new SqlFileSource(new SqlFileSourceOptions
                {
                    FilePattern = _sqlDir.Replace('\\', '/') + "/**/*.sql",
                    CommentScope = CommentScope.All,
                    ErrorMode = ParseErrorMode.Skip,
                })
            ],
        });

        _app.StartAsync().GetAwaiter().GetResult();

        var serverAddress = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(serverAddress) };
        _client.Timeout = TimeSpan.FromHours(1);
    }

    private static void WriteSqlFiles(string dir, string subDir)
    {
        // Basic SELECT query (no auth)
        File.WriteAllText(Path.Combine(dir, "get_time.sql"), """
            SELECT now() as current_time;
            """);

        // SELECT with parameter
        File.WriteAllText(Path.Combine(dir, "get_by_id.sql"), """
            -- @param $1 id
            SELECT id, name, active FROM sql_describe_test WHERE id = $1;
            """);

        // SELECT with multiple params
        File.WriteAllText(Path.Combine(dir, "search_test.sql"), """
            -- @param $1 name_filter
            -- @param $2 active_filter
            SELECT id, name, active FROM sql_describe_test WHERE name LIKE $1 AND active = $2;
            """);

        // INSERT mutation
        File.WriteAllText(Path.Combine(dir, "insert_test.sql"), """
            -- @param $1 id
            -- @param $2 name
            INSERT INTO sql_describe_test (id, name) VALUES ($1, $2) RETURNING id, name;
            """);

        // UPDATE mutation
        File.WriteAllText(Path.Combine(dir, "update_test.sql"), """
            -- @param $1 new_name
            -- @param $2 id
            UPDATE sql_describe_test SET name = $1 WHERE id = $2 RETURNING id, name;
            """);

        // DELETE mutation
        File.WriteAllText(Path.Combine(dir, "delete_test.sql"), """
            -- @param $1 id
            DELETE FROM sql_describe_test WHERE id = $1 RETURNING id;
            """);

        // DO block
        File.WriteAllText(Path.Combine(dir, "do_block.sql"), """
            DO $$ BEGIN PERFORM 1; END; $$;
            """);

        // Parameterless SELECT
        File.WriteAllText(Path.Combine(dir, "count_test.sql"), """
            SELECT count(*) as total FROM sql_describe_test;
            """);

        // File in subdirectory
        File.WriteAllText(Path.Combine(subDir, "sub_query.sql"), """
            SELECT 42 as answer;
            """);

        // With comment annotations
        File.WriteAllText(Path.Combine(dir, "annotated_query.sql"), """
            -- HTTP GET
            -- @param $1 from_date
            -- @param $2 to_date
            SELECT id, name, created_at FROM sql_describe_test WHERE created_at BETWEEN $1 AND $2;
            """);

        // Custom type — whole composite column in return
        File.WriteAllText(Path.Combine(dir, "custom_type_return.sql"), """
            -- @param $1 id
            SELECT id, data FROM sql_file_custom_table WHERE id = $1;
            """);

        // Array of custom types
        File.WriteAllText(Path.Combine(dir, "custom_array_query.sql"), """
            -- @param $1 id
            SELECT id, items FROM sql_file_custom_array_table WHERE id = $1;
            """);

        // Custom type fields expanded in return
        File.WriteAllText(Path.Combine(dir, "custom_type_fields.sql"), """
            -- @param $1 id
            SELECT id, (data).val1, (data).val2, (data).val3 FROM sql_file_custom_table WHERE id = $1;
            """);
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();

        // Clean up temp SQL files
        try
        {
            if (Directory.Exists(_sqlDir))
            {
                Directory.Delete(_sqlDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
