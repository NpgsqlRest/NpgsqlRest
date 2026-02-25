using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void DecryptScalarTests()
    {
        script.Append(@"
create table dp_scalar_secrets (
    id int primary key,
    secret text not null
);

create function dp_get_scalar_secret(_id int)
returns text
language plpgsql
as
$$
begin
    return (select secret from dp_scalar_secrets where id = _id);
end;
$$;

comment on function dp_get_scalar_secret(int) is '
decrypt
';
");
    }
}

[Collection("TestFixture")]
public class DecryptScalarTests(TestFixture test)
{
    [Fact]
    public async Task Test_decrypt_scalar_result_returns_plaintext()
    {
        var protector = NpgsqlRestTests.Setup.Program.DataProtector!;
        var encrypted = protector.Protect("scalar-secret");

        // Insert pre-encrypted row
        using var conn = Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "insert into dp_scalar_secrets (id, secret) values (1, $1) on conflict (id) do update set secret = $1",
            conn);
        cmd.Parameters.AddWithValue(encrypted);
        await cmd.ExecuteNonQueryAsync();

        using var result = await test.Client.GetAsync("/api/dp-get-scalar-secret/?id=1");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        // Scalar result should be decrypted
        response.Should().Be("scalar-secret");
    }
}
