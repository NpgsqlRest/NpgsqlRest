using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void DecryptAllColumnsTests()
    {
        script.Append(@"
create table dp_all_secrets (
    a text not null,
    b text not null
);

create function dp_get_all_decrypted()
returns table(a text, b text)
language plpgsql
as
$$
begin
    return query select s.a, s.b from dp_all_secrets s limit 1;
end;
$$;

comment on function dp_get_all_decrypted() is '
decrypt
';
");
    }
}

[Collection("TestFixture")]
public class DecryptAllColumnsTests(TestFixture test)
{
    [Fact]
    public async Task Test_decrypt_all_columns_returns_plaintext()
    {
        var protector = NpgsqlRestTests.Setup.Program.DataProtector!;
        var encA = protector.Protect("plaintext-a");
        var encB = protector.Protect("plaintext-b");

        // Insert pre-encrypted row
        using var conn = Database.CreateConnection();
        await conn.OpenAsync();
        await using var delCmd = new NpgsqlCommand("delete from dp_all_secrets", conn);
        await delCmd.ExecuteNonQueryAsync();
        await using var cmd = new NpgsqlCommand(
            "insert into dp_all_secrets (a, b) values ($1, $2)",
            conn);
        cmd.Parameters.AddWithValue(encA);
        cmd.Parameters.AddWithValue(encB);
        await cmd.ExecuteNonQueryAsync();

        using var result = await test.Client.GetAsync("/api/dp-get-all-decrypted/");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(response);
        var row = json!.AsArray()[0]!;

        row["a"]!.ToString().Should().Be("plaintext-a");
        row["b"]!.ToString().Should().Be("plaintext-b");
    }
}
