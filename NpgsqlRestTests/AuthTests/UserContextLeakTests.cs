using System.Net;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void UserContextLeakTests()
    {
        script.Append("""
        create function ucl_get_context()
        returns text
        language sql as $$
        select
            coalesce(current_setting('request.user_id', true), '<null>') || '|' ||
            coalesce(current_setting('request.user_name', true), '<null>') || '|' ||
            coalesce(current_setting('request.user_claims', true), '<null>')
        $$;
        comment on function ucl_get_context() is '
        HTTP GET
        user_context
        ';
        """);
    }
}

/// <summary>
/// Security regression: an anonymous user_context request must never run with the previous request's user identity,
/// even when the pooled connection is not reset between requests.
/// </summary>
[Collection("UserContextLeakTestFixture")]
public class UserContextLeakTests(UserContextLeakTestFixture test)
{
    [Fact]
    public async Task Anonymous_request_does_not_inherit_previous_users_context()
    {
        using var userHandler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        using var user = new HttpClient(userHandler) { BaseAddress = new Uri(test.ServerAddress) };
        using var anonymous = new HttpClient { BaseAddress = new Uri(test.ServerAddress) };

        using (var login = await user.GetAsync("/ucl-login"))
        {
            login.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // authenticated request sets the context on the only pooled connection
        using (var response = await user.GetAsync("/api/ucl-get-context"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                "123|admin|{\"user_id\":\"123\",\"user_name\":\"admin\"}");
        }

        // the next, anonymous request gets the same connection and must not see that identity
        using (var response = await anonymous.GetAsync("/api/ucl-get-context"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("||");
        }
    }
}
