using NpgsqlRest.Auth;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void LoginUserEnumerationTests()
    {
        script.Append("""
        create table login_enum_users (username text primary key, password_hash text not null);

        create function login_enum(_username text, _pass text)
        returns table (name_identifier text, name text, hash text)
        language sql as $$
        select username, username, password_hash from login_enum_users where username = _username
        $$;
        comment on function login_enum(text, text) is 'login';
        """);
    }
}

/// <summary>
/// Security regression (user enumeration): a wrong password and an unknown user get the same response, and the
/// unknown user costs the same password-hash verification. A wrong password used to answer 404 while an unknown user
/// answered 401 - and only the existing user paid for the (deliberately slow) hash check.
/// </summary>
[Collection("TestFixture")]
public class LoginUserEnumerationTests(TestFixture test)
{
    private sealed class CountingHasher(IPasswordHasher inner) : IPasswordHasher
    {
        public int Verifications;
        public string HashPassword(string password) => inner.HashPassword(password);
        public bool VerifyHashedPassword(string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref Verifications);
            return inner.VerifyHashedPassword(hashedPassword, providedPassword);
        }
    }

    private async Task<(HttpStatusCode Status, string Body)> LoginAsync(string username, string password)
    {
        using var client = test.Application.CreateClient();
        using var content = new StringContent($"{{\"username\":\"{username}\",\"pass\":\"{password}\"}}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/login-enum/", content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_are_indistinguishable()
    {
        var options = NpgsqlRestOptions.Options.AuthenticationOptions;
        var original = options.PasswordHasher!;
        var counting = new CountingHasher(original);
        await using (var connection = Database.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "insert into login_enum_users values ('known', $1) on conflict do nothing";
            command.Parameters.Add(new Npgsql.NpgsqlParameter { Value = original.HashPassword("right-password") });
            await command.ExecuteNonQueryAsync();
        }

        options.PasswordHasher = counting;
        try
        {
            var wrongPassword = await LoginAsync("known", "wrong-password");
            var verificationsForKnownUser = counting.Verifications;
            var unknownUser = await LoginAsync("nobody", "wrong-password");

            wrongPassword.Status.Should().Be(HttpStatusCode.Unauthorized);
            wrongPassword.Body.Should().Be("{\"title\":\"Unauthorized\",\"status\":401}");
            unknownUser.Should().Be(wrongPassword);

            verificationsForKnownUser.Should().Be(1);
            (counting.Verifications - verificationsForKnownUser).Should().Be(1, "an unknown user costs the same hash verification");

            var correct = await LoginAsync("known", "right-password");
            correct.Status.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            options.PasswordHasher = original;
            // the attempts ran PasswordVerificationFailed/SucceededCommand; other tests read those tables
            await using var connection = Database.CreateConnection();
            await connection.OpenAsync();
            await using var cleanup = connection.CreateCommand();
            cleanup.CommandText = "delete from failed_logins where user_id = 'known'; delete from succeeded_logins where user_id = 'known'";
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
