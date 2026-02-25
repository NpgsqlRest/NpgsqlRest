namespace NpgsqlRestTests;

public static partial class Database
{
    public static void EncryptDecryptRoundtripTests()
    {
        script.Append(@"
create table dp_roundtrip (
    key text primary key,
    value text not null
);

create function dp_store_protected(_key text, _value text)
returns void
language plpgsql
as
$$
begin
    insert into dp_roundtrip (key, value) values (_key, _value)
    on conflict (key) do update set value = excluded.value;
end;
$$;

comment on function dp_store_protected(text, text) is '
HTTP POST
encrypt _value
';

create function dp_get_protected(_key text)
returns table(key text, value text)
language plpgsql
as
$$
begin
    return query select r.key, r.value from dp_roundtrip r where r.key = _key;
end;
$$;

comment on function dp_get_protected(text) is '
decrypt value
';
");
    }
}

[Collection("TestFixture")]
public class EncryptDecryptRoundtripTests(TestFixture test)
{
    [Fact]
    public async Task Test_roundtrip_encrypt_then_decrypt_returns_original()
    {
        // Store: encrypt _value before writing to DB
        using var storeContent = new StringContent(
            "{\"key\": \"roundtrip-key\", \"value\": \"sensitive-data-123\"}",
            Encoding.UTF8,
            "application/json");
        using var storeResult = await test.Client.PostAsync("/api/dp-store-protected/", storeContent);
        storeResult.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Retrieve: decrypt value column when reading from DB
        using var getResult = await test.Client.GetAsync("/api/dp-get-protected/?key=roundtrip-key");
        getResult.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await getResult.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(response);
        var row = json!.AsArray()[0]!;

        // The key was not encrypted, so it stays as-is
        row["key"]!.ToString().Should().Be("roundtrip-key");
        // The value was encrypted on write and decrypted on read — roundtrip should match
        row["value"]!.ToString().Should().Be("sensitive-data-123");
    }
}
