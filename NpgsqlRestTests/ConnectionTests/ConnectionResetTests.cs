using Npgsql;
using NpgsqlRestClient;

namespace NpgsqlRestTests.ConnectionTests;

/// <summary>
/// The npgsqlrest client builds its data sources in two variants and uses one of them for requests:
/// - with the connection reset, when a request can leave session-level settings on its connection (user_context, for
///   example). The reset clears them when the connection goes back to the pool, without discarding the cached plans
///   of PL/pgSQL functions, which Npgsql's default reset (DISCARD ALL) does on every reuse;
/// - without the reset, when nothing can stay on a connection.
/// "No Reset On Close" in the connection string overrides both.
/// </summary>
public class ConnectionResetTests
{
    private static Builder NewBuilder()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "{}");
        var config = new Config();
        try
        {
            config.Build([tempFile], []);
        }
        finally
        {
            File.Delete(tempFile);
        }
        return new Builder(config);
    }

    // One physical connection, so the second OpenConnection reuses the first one's session.
    // "No Reset On Close" is written only when true: setting the property at all puts the key into the string, and a
    // connection string that has the key is left alone by the client (see the last tests).
    private static string ConnectionString(bool noResetOnClose)
    {
        var builder = new NpgsqlConnectionStringBuilder(Database.Create()) { MaxPoolSize = 1 };
        if (noResetOnClose)
        {
            builder.NoResetOnClose = true;
        }
        return builder.ConnectionString;
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T?)await command.ExecuteScalarAsync();
    }

    [Fact]
    public async Task Reset_clears_session_settings_and_keeps_prepared_statements()
    {
        await using var dataSource = NewBuilder().BuildMainDataSource(ConnectionString(noResetOnClose: false));

        int processId;
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            processId = connection.ProcessID;
            await ScalarAsync<string>(connection, "select set_config('reset_test.user_id', 'alice', false)");
            (await ScalarAsync<string>(connection, "select current_setting('reset_test.user_id', true)")).Should().Be("alice");
            // the statement the client prepares on every physical connection
            (await ScalarAsync<long>(connection, "select count(*) from pg_prepared_statements")).Should().Be(1);
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            connection.ProcessID.Should().Be(processId, "the pool has one connection");
            // the previous request's session setting is gone
            (await ScalarAsync<string>(connection, "select current_setting('reset_test.user_id', true)")).Should().Be("");
            // DISCARD ALL would have deallocated it (and discarded every cached plan with it)
            (await ScalarAsync<long>(connection, "select count(*) from pg_prepared_statements")).Should().Be(1);
        }
    }

    [Fact]
    public async Task Variant_without_reset_keeps_session_settings_and_prepares_nothing()
    {
        await using var dataSource = NewBuilder().BuildMainDataSource(ConnectionString(noResetOnClose: false), skipConnectionReset: true);

        int processId;
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            processId = connection.ProcessID;
            await ScalarAsync<string>(connection, "select set_config('reset_test.user_id', 'carol', false)");
            (await ScalarAsync<long>(connection, "select count(*) from pg_prepared_statements")).Should().Be(0);
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            connection.ProcessID.Should().Be(processId, "the pool has one connection");
            // this is why the variant is only used when no endpoint leaves session-level settings behind
            (await ScalarAsync<string>(connection, "select current_setting('reset_test.user_id', true)")).Should().Be("carol");
        }
    }

    [Fact]
    public async Task Connection_string_that_asks_for_the_reset_gets_it_in_both_variants()
    {
        var connectionString = ConnectionString(noResetOnClose: false) + ";No Reset On Close=false";
        await using var dataSource = NewBuilder().BuildMainDataSource(connectionString, skipConnectionReset: true);

        int processId;
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            processId = connection.ProcessID;
            await ScalarAsync<string>(connection, "select set_config('reset_test.user_id', 'dave', false)");
            (await ScalarAsync<long>(connection, "select count(*) from pg_prepared_statements")).Should().Be(1);
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            connection.ProcessID.Should().Be(processId, "the pool has one connection");
            (await ScalarAsync<string>(connection, "select current_setting('reset_test.user_id', true)")).Should().Be("");
        }
    }

    [Theory]
    [InlineData("Host=localhost;Database=db;Username=u;Password=p", false)]
    [InlineData("Host=localhost;Database=db;Username=u;Password=p;No Reset On Close=true", true)]
    [InlineData("Host=localhost;Database=db;Username=u;Password=p;No Reset On Close=false", true)]
    [InlineData("Host=localhost;Database=db;Username=u;Password=p;NoResetOnClose=True", true)]
    [InlineData("Host=localhost;Database=db;Username=u;Password=p;no reset on close=FALSE", true)]
    public void Detects_a_connection_string_that_sets_the_reset_itself(string connectionString, bool expected)
    {
        Builder.IsConnectionResetConfigured(connectionString).Should().Be(expected);
    }

    [Fact]
    public async Task No_reset_on_close_is_respected()
    {
        await using var dataSource = NewBuilder().BuildMainDataSource(ConnectionString(noResetOnClose: true));

        int processId;
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            processId = connection.ProcessID;
            await ScalarAsync<string>(connection, "select set_config('reset_test.user_id', 'bob', false)");
            // nothing is prepared when connections are not reset
            (await ScalarAsync<long>(connection, "select count(*) from pg_prepared_statements")).Should().Be(0);
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            connection.ProcessID.Should().Be(processId, "the pool has one connection");
            // not reset: the setting is still there (why "No Reset On Close" must be a deliberate choice)
            (await ScalarAsync<string>(connection, "select current_setting('reset_test.user_id', true)")).Should().Be("bob");
        }
    }
}
