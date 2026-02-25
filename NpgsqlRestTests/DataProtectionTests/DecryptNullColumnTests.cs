using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void DecryptNullColumnTests()
    {
        script.Append(@"
create table dp_nullable_secrets (
    id int primary key,
    secret text
);

create function dp_get_nullable_secret(_id int)
returns table(id int, secret text)
language plpgsql
as
$$
begin
    return query select s.id, s.secret from dp_nullable_secrets s where s.id = _id;
end;
$$;

comment on function dp_get_nullable_secret(int) is '
decrypt secret
';
");
    }
}

[Collection("TestFixture")]
public class DecryptNullColumnTests(TestFixture test)
{
    [Fact]
    public async Task Test_decrypt_null_column_returns_null()
    {
        // Insert a row with NULL secret
        using var conn = Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "insert into dp_nullable_secrets (id, secret) values (1, null) on conflict (id) do update set secret = null",
            conn);
        await cmd.ExecuteNonQueryAsync();

        using var result = await test.Client.GetAsync("/api/dp-get-nullable-secret/?id=1");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(response);
        var row = json!.AsArray()[0]!;

        // NULL should pass through as JSON null, not cause a decryption error
        row["secret"].Should().BeNull();
    }
}
