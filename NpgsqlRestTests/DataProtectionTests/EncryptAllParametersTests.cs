namespace NpgsqlRestTests;

public static partial class Database
{
    public static void EncryptAllParametersTests()
    {
        script.Append(@"
create function dp_encrypt_all(_a text, _b text)
returns table(a text, b text)
language plpgsql
as
$$
begin
    return query select _a, _b;
end;
$$;

comment on function dp_encrypt_all(text, text) is '
HTTP POST
encrypt
';
");
    }
}

[Collection("TestFixture")]
public class EncryptAllParametersTests(TestFixture test)
{
    [Fact]
    public async Task Test_encrypt_all_parameters_both_encrypted()
    {
        using var content = new StringContent("{\"a\": \"val1\", \"b\": \"val2\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/dp-encrypt-all/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(response);

        json.Should().NotBeNull();
        var row = json!.AsArray()[0]!;

        // Both values should be encrypted (not plaintext)
        row["a"]!.ToString().Should().NotBe("val1");
        row["b"]!.ToString().Should().NotBe("val2");
        // Ciphertext should be longer than original
        row["a"]!.ToString().Length.Should().BeGreaterThan("val1".Length);
        row["b"]!.ToString().Length.Should().BeGreaterThan("val2".Length);
    }
}
