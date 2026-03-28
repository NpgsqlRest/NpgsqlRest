using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileAdvancedFixture")]
public class SqlFileEncryptionTests(SqlFileAdvancedFixture test)
{
    [Fact]
    public async Task SqlFile_EncryptAll_ParametersAreEncryptedBeforeReachingPostgres()
    {
        using var content = new StringContent("{\"a\": \"val1\", \"b\": \"val2\"}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-encrypt-all", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var json = JsonNode.Parse(body);
        var row = json!.AsArray()[0]!;

        // Encrypted values should NOT equal plaintext
        row["a"]!.ToString().Should().NotBe("val1");
        row["b"]!.ToString().Should().NotBe("val2");

        // Encrypted values should be longer than plaintext (encryption adds overhead)
        row["a"]!.ToString().Length.Should().BeGreaterThan("val1".Length);
        row["b"]!.ToString().Length.Should().BeGreaterThan("val2".Length);
    }

    [Fact]
    public async Task SqlFile_EncryptNamed_OnlySpecifiedParamIsEncrypted()
    {
        using var content = new StringContent("{\"secret\": \"hidden\", \"plain\": \"visible\"}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-encrypt-named", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var json = JsonNode.Parse(body);
        var row = json!.AsArray()[0]!;

        // Secret should be encrypted (not equal to plaintext)
        row["secret"]!.ToString().Should().NotBe("hidden");
        row["secret"]!.ToString().Length.Should().BeGreaterThan("hidden".Length);

        // Plain should remain unencrypted
        row["plain"]!.ToString().Should().Be("visible");
    }

    [Fact]
    public async Task SqlFile_DecryptAll_EncryptedValuesAreDecryptedInResponse()
    {
        // Pre-encrypt values
        var encA = test.DataProtector.Protect("plaintext-a");
        var encB = test.DataProtector.Protect("plaintext-b");

        using var content = new StringContent(
            $"{{\"a\": \"{encA}\", \"b\": \"{encB}\"}}",
            Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-decrypt-all", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var json = JsonNode.Parse(body);
        var row = json!.AsArray()[0]!;

        // Decrypted values should equal original plaintext
        row["a"]!.ToString().Should().Be("plaintext-a");
        row["b"]!.ToString().Should().Be("plaintext-b");
    }
}
