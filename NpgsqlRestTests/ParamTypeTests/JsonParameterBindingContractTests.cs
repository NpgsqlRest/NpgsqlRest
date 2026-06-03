using Npgsql;
using NpgsqlTypes;

namespace NpgsqlRestTests.ParamTypeTests;

/// <summary>
/// Locks the PostgreSQL/Npgsql parameter-binding mechanism that the JSON-payload bindings in
/// <c>ExternalAuth</c>, the Fido2 endpoints and the upload handlers depend on.
///
/// Today those sites bind a JSON string with <c>NpgsqlDbType.Json</c>. PostgreSQL function-overload
/// resolution then matches ONLY a <c>json</c>-typed parameter; a <c>jsonb</c> or <c>text</c> parameter
/// throws <c>42883 "function does not exist"</c> (no implicit cast from json to jsonb/text). The planned
/// fix switches those bindings to <c>NpgsqlDbType.Unknown</c>, which is resolved server-side through the
/// target type's input function and therefore matches <c>json</c>, <c>jsonb</c> AND <c>text</c>.
///
/// These tests prove both halves against a real connection, so the fix's premise cannot silently drift
/// (e.g. a future Npgsql/PostgreSQL change to coercion rules). They exercise the platform mechanism, not
/// NpgsqlRest code, so they are GREEN both before and after the fix - a permanent guard.
/// </summary>
public class JsonParameterBindingContractTests
{
    private const string Payload = "{\"k\":1}";

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(Database.GetIinitialConnectionString());
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// Creates a single-overload temp function <c>pg_temp.contract_&lt;suffix&gt;(a text, b &lt;pgType&gt;)</c>
    /// that echoes <c>b::text</c>, then calls it binding <c>b</c> = <paramref name="value"/> with the given
    /// NpgsqlDbType. Distinct per-type names avoid creating overloads that would muddy resolution.
    /// </summary>
    private static async Task<string?> CallWithBinding(
        NpgsqlConnection conn, string pgType, object value, NpgsqlDbType? bindType)
    {
        var suffix = pgType.Replace(" ", "_");
        await using (var create = conn.CreateCommand())
        {
            create.CommandText =
                $"create or replace function pg_temp.contract_{suffix}(a text, b {pgType}) " +
                "returns text language sql as $$ select b::text $$;";
            await create.ExecuteNonQueryAsync();
        }
        await using var call = conn.CreateCommand();
        call.CommandText = $"select pg_temp.contract_{suffix}($1, $2)";
        call.Parameters.Add(new NpgsqlParameter { Value = "p", NpgsqlDbType = NpgsqlDbType.Text });
        var p = new NpgsqlParameter { Value = value };
        if (bindType.HasValue)
        {
            p.NpgsqlDbType = bindType.Value;
        }
        call.Parameters.Add(p);
        var result = await call.ExecuteScalarAsync();
        return result is null || result is DBNull ? null : (string)result;
    }

    // ---- Today's behaviour: NpgsqlDbType.Json matches ONLY a json parameter ----

    [Fact]
    public async Task JsonBinding_Matches_JsonParameter()
    {
        await using var conn = await OpenAsync();
        (await CallWithBinding(conn, "json", Payload, NpgsqlDbType.Json)).Should().Be("{\"k\":1}");
    }

    [Fact]
    public async Task JsonBinding_Fails_Against_JsonbParameter_42883()
    {
        await using var conn = await OpenAsync();
        var act = async () => await CallWithBinding(conn, "jsonb", Payload, NpgsqlDbType.Json);
        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42883");
    }

    [Fact]
    public async Task JsonBinding_Fails_Against_TextParameter_42883()
    {
        await using var conn = await OpenAsync();
        var act = async () => await CallWithBinding(conn, "text", Payload, NpgsqlDbType.Json);
        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42883");
    }

    // ---- The fix: NpgsqlDbType.Unknown matches json, jsonb AND text ----

    [Fact]
    public async Task UnknownBinding_Matches_JsonParameter()
    {
        await using var conn = await OpenAsync();
        (await CallWithBinding(conn, "json", Payload, NpgsqlDbType.Unknown)).Should().Be("{\"k\":1}");
    }

    [Fact]
    public async Task UnknownBinding_Matches_JsonbParameter()
    {
        await using var conn = await OpenAsync();
        // jsonb normalises whitespace on output
        (await CallWithBinding(conn, "jsonb", Payload, NpgsqlDbType.Unknown)).Should().Be("{\"k\": 1}");
    }

    [Fact]
    public async Task UnknownBinding_Matches_TextParameter()
    {
        await using var conn = await OpenAsync();
        (await CallWithBinding(conn, "text", Payload, NpgsqlDbType.Unknown)).Should().Be("{\"k\":1}");
    }

    // ---- NULL handling: the current code passes DBNull for absent payloads ----

    [Theory]
    [InlineData("json")]
    [InlineData("jsonb")]
    [InlineData("text")]
    public async Task UnknownBinding_WithDbNull_PassesNull(string pgType)
    {
        await using var conn = await OpenAsync();
        (await CallWithBinding(conn, pgType, DBNull.Value, NpgsqlDbType.Unknown)).Should().BeNull();
    }

    // ---- Round-trip integrity: nested quotes / arrays survive the unknown binding ----

    [Fact]
    public async Task UnknownBinding_PreservesNestedQuotesAndArrays_IntoJsonb()
    {
        await using var conn = await OpenAsync();
        const string payload = "{\"name\":\"O'Brien\",\"arr\":[1,2,3]}";
        var result = await CallWithBinding(conn, "jsonb", payload, NpgsqlDbType.Unknown);
        result.Should().Be("{\"arr\": [1, 2, 3], \"name\": \"O'Brien\"}");
    }
}
