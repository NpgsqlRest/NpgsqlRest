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

        -- A procedure can commit, so its context is not sent with it as local settings (which a commit would end):
        -- it is set at session level, by a separate batch before the procedure runs.
        create table ucl_log (tag text primary key, context text);
        create procedure ucl_proc_context(_tag text)
        language plpgsql as $$
        begin
            insert into ucl_log values (_tag, ucl_get_context());
            commit;
            insert into ucl_log values (_tag || ' after commit', ucl_get_context());
        end $$;
        comment on procedure ucl_proc_context(text) is '
        HTTP POST
        user_context
        ';

        create function ucl_log_get(_tag text) returns text language sql as $$ select context from ucl_log where tag = _tag $$;
        comment on function ucl_log_get(text) is 'HTTP GET';
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

    private static async Task CallProcedureAsync(HttpClient client, string tag)
    {
        using var body = new StringContent("{\"tag\":\"" + tag + "\"}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/ucl-proc-context", body);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> LoggedContextAsync(HttpClient client, string tag)
    {
        using var response = await client.GetAsync("/api/ucl-log-get?tag=" + Uri.EscapeDataString(tag));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Procedures keep the session-level context (set by a separate batch before the procedure): it has to survive
    /// the procedure's own commits. The same guarantee applies there - an anonymous request sets every key to NULL.
    /// </summary>
    [Fact]
    public async Task Procedure_context_survives_its_commit_and_anonymous_request_does_not_inherit_it()
    {
        using var userHandler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        using var user = new HttpClient(userHandler) { BaseAddress = new Uri(test.ServerAddress) };
        using var anonymous = new HttpClient { BaseAddress = new Uri(test.ServerAddress) };

        using (var login = await user.GetAsync("/ucl-login"))
        {
            login.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await CallProcedureAsync(user, "user");
        (await LoggedContextAsync(anonymous, "user")).Should().Be("123|admin|{\"user_id\":\"123\",\"user_name\":\"admin\"}");
        (await LoggedContextAsync(anonymous, "user after commit")).Should().Be("123|admin|{\"user_id\":\"123\",\"user_name\":\"admin\"}");

        // The user's settings are still on the only (never reset) connection. A function's context is sent with the
        // function, local to that batch, and overrides them: the anonymous request does not see the user.
        using (var response = await anonymous.GetAsync("/api/ucl-get-context"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("||");
        }

        // and an anonymous call of the procedure overwrites them at session level
        await CallProcedureAsync(anonymous, "anonymous");
        (await LoggedContextAsync(anonymous, "anonymous")).Should().Be("||");
        (await LoggedContextAsync(anonymous, "anonymous after commit")).Should().Be("||");
    }
}
