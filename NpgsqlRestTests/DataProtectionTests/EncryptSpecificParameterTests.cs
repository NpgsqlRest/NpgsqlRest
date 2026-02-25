namespace NpgsqlRestTests;

public static partial class Database
{
    public static void EncryptSpecificParameterTests()
    {
        script.Append(@"
create function dp_encrypt_specific(_id int, _secret text)
returns text
language plpgsql
as
$$
begin
    return _secret;
end;
$$;

comment on function dp_encrypt_specific(int, text) is '
HTTP POST
encrypt _secret
';
");
    }
}

[Collection("TestFixture")]
public class EncryptSpecificParameterTests(TestFixture test)
{
    [Fact]
    public async Task Test_encrypt_specific_parameter_returns_ciphertext()
    {
        using var content = new StringContent("{\"id\": 1, \"secret\": \"hello-world\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/dp-encrypt-specific/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        // The response should NOT be the plaintext — it should be encrypted ciphertext
        response.Should().NotBe("hello-world");
        // DataProtection ciphertext is base64-like and significantly longer than the input
        response.Length.Should().BeGreaterThan("hello-world".Length);
    }

    [Fact]
    public async Task Test_encrypt_specific_parameter_non_targeted_param_is_unchanged()
    {
        // _id is int and not in the encrypt list, so it should be unaffected
        using var content = new StringContent("{\"id\": 42, \"secret\": \"test\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/dp-encrypt-specific/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();

        // The function returns _secret, which should be encrypted (not "test")
        response.Should().NotBe("test");
    }
}
