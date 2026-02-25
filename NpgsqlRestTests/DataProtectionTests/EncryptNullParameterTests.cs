namespace NpgsqlRestTests;

public static partial class Database
{
    public static void EncryptNullParameterTests()
    {
        script.Append(@"
create function dp_encrypt_nullable(_secret text)
returns text
language plpgsql
as
$$
begin
    return _secret;
end;
$$;

comment on function dp_encrypt_nullable(text) is '
HTTP POST
encrypt _secret
';
");
    }
}

[Collection("TestFixture")]
public class EncryptNullParameterTests(TestFixture test)
{
    [Fact]
    public async Task Test_encrypt_null_parameter_passes_through_as_null()
    {
        using var content = new StringContent("{\"secret\": null}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/dp-encrypt-nullable/", content);

        // Null returns empty string with 200 OK (default null handling)
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        // Should be empty (null value not encrypted, returned as-is)
        response.Should().BeEmpty();
    }

    [Fact]
    public async Task Test_encrypt_non_null_parameter_is_encrypted()
    {
        using var content = new StringContent("{\"secret\": \"plaintext\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/dp-encrypt-nullable/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        // Should be encrypted (not plaintext)
        response.Should().NotBe("plaintext");
        response.Length.Should().BeGreaterThan("plaintext".Length);
    }
}
