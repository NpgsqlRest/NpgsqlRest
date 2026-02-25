using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void DecryptSpecificColumnTests()
    {
        script.Append(@"
create table dp_secrets (
    id int primary key,
    secret text not null,
    label text not null
);

create function dp_get_decrypted_secret(_id int)
returns table(id int, secret text, label text)
language plpgsql
as
$$
begin
    return query select s.id, s.secret, s.label from dp_secrets s where s.id = _id;
end;
$$;

comment on function dp_get_decrypted_secret(int) is '
decrypt secret
';
");
    }
}

[Collection("TestFixture")]
public class DecryptSpecificColumnTests(TestFixture test)
{
    [Fact]
    public async Task Test_decrypt_specific_column_returns_plaintext()
    {
        var protector = NpgsqlRestTests.Setup.Program.DataProtector!;
        var encrypted = protector.Protect("my-secret-value");

        // Insert pre-encrypted row directly into the database
        using var conn = Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "insert into dp_secrets (id, secret, label) values (1, $1, $2) on conflict (id) do update set secret = $1, label = $2",
            conn);
        cmd.Parameters.AddWithValue(encrypted);
        cmd.Parameters.AddWithValue("visible-label");
        await cmd.ExecuteNonQueryAsync();

        using var result = await test.Client.GetAsync("/api/dp-get-decrypted-secret/?id=1");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(response);
        var row = json!.AsArray()[0]!;

        // The secret column should be decrypted to plaintext
        row["secret"]!.ToString().Should().Be("my-secret-value");
        // The label column is not in the decrypt list, so it stays as-is
        row["label"]!.ToString().Should().Be("visible-label");
    }
}
