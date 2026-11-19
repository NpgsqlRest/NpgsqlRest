using NpgsqlRest.Auth;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void SerializeLoginResponseHashTests()
    {
        script.Append("""
        create function serialize_login_hash(_pass text, _hashed text)
        returns table (name_identifier int, name text, hash text)
        language sql as $$
        select 777, 'serialized', _hashed
        $$;
        comment on function serialize_login_hash(text, text) is 'login';
        """);
    }
}

/// <summary>
/// Security regression: with AuthenticationOptions.SerializeAuthEndpointsResponse on, a successful cookie login returns
/// the login routine's record - which used to include the password hash column needed for verification. The hash is
/// now written as null.
/// </summary>
[Collection("TestFixture")]
public class SerializeLoginResponseHashTests(TestFixture test)
{
    [Fact]
    public async Task Serialized_login_response_does_not_contain_the_hash()
    {
        var options = NpgsqlRestOptions.Options.AuthenticationOptions;
        var saved = options.SerializeAuthEndpointsResponse;
        options.SerializeAuthEndpointsResponse = true;
        try
        {
            using var client = test.Application.CreateClient();
            var password = "MySecurePass123!";
            var hashed = new PasswordHasher().HashPassword(password);
            using var content = new StringContent($"{{\"pass\": \"{password}\", \"hashed\": \"{hashed}\"}}", Encoding.UTF8, "application/json");

            using var login = await client.PostAsync("/api/serialize-login-hash/", content);
            login.StatusCode.Should().Be(HttpStatusCode.OK);
            (await login.Content.ReadAsStringAsync()).Should().Be("[{\"nameIdentifier\":777,\"name\":\"serialized\",\"hash\":null}]");
        }
        finally
        {
            options.SerializeAuthEndpointsResponse = saved;
        }
    }
}
