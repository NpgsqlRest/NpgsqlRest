namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void SseScopeTests()
        {
            script.Append("""

        -- Anonymous-subscribable event stream; per-event filtering comes from publish-side hints.
        create function ssecope_events()
        returns void
        language plpgsql immutable
        as $$ begin perform 1; end $$;
        comment on function ssecope_events() is '
        HTTP GET
        sse_subscribe
        ';

        -- Publisher: emits the message with an optional scope hint (mirrors the documented
        -- RAISE ... USING HINT = ''authorize <role>'' production pattern).
        create procedure ssecope_publish(_msg text, _hint text)
        language plpgsql
        as $$
        begin
            if _hint is null then
                raise info '%', _msg;
            else
                raise info '%', _msg using hint = _hint;
            end if;
        end $$;
        comment on procedure ssecope_publish(text, text) is '
        HTTP POST
        sse_publish
        ';

        create function ssecope_login_a()
        returns table (name_identifier int, name text, role text[])
        language sql as $$
        select 9001, 'scope_user_a', array['role_a']
        $$;
        comment on function ssecope_login_a() is 'login';

        create function ssecope_login_b()
        returns table (name_identifier int, name text, role text[])
        language sql as $$
        select 9002, 'scope_user_b', array['role_b']
        $$;
        comment on function ssecope_login_b() is 'login';
        """);
        }
    }
}

namespace NpgsqlRestTests.SseTests
{
    using NpgsqlRestTests.Setup;

    [Collection("TestFixture")]
    public class SseScopeTests(TestFixture test)
    {
        private const string SubscribeUrl = "/api/ssecope-events/info";
        private const string PublishUrl = "/api/ssecope-publish";

        private static StringContent Publish(string msg, string? hint) =>
            new(hint is null
                ? $"{{\"msg\":\"{msg}\",\"hint\":null}}"
                : $"{{\"msg\":\"{msg}\",\"hint\":\"{hint}\"}}",
                Encoding.UTF8, "application/json");

        /// <summary>
        /// Per-event scoping via RAISE ... USING HINT: 'authorize role_a' must deliver ONLY to
        /// subscribers whose claims include role_a; bare 'authorize' must deliver only to
        /// authenticated subscribers. Non-delivery is proven by ordering, not by waiting: each
        /// excluded subscriber's FIRST received event must be the later, broader one.
        /// </summary>
        [Fact]
        public async Task Hint_Scoped_Events_Are_Delivered_Only_To_Matching_Subscribers()
        {
            // Three subscribers: A (role_a), B (role_b), Anon (not authenticated).
            using var clientA = test.Application.CreateClient();
            using var loginA = await clientA.PostAsync("/api/ssecope-login-a/", null);
            loginA.StatusCode.Should().Be(HttpStatusCode.OK);

            using var clientB = test.Application.CreateClient();
            using var loginB = await clientB.PostAsync("/api/ssecope-login-b/", null);
            loginB.StatusCode.Should().Be(HttpStatusCode.OK);

            using var clientAnon = test.Application.CreateClient();

            await using var sseA = await SseTestClient.OpenAsync(clientA, SubscribeUrl);
            await sseA.WaitForRegisteredAsync();
            await using var sseB = await SseTestClient.OpenAsync(clientB, SubscribeUrl);
            await sseB.WaitForRegisteredAsync();
            await using var sseAnon = await SseTestClient.OpenAsync(clientAnon, SubscribeUrl);
            await sseAnon.WaitForRegisteredAsync();

            // 1. Scoped to role_a only.
            using (var p = await clientA.PostAsync(PublishUrl, Publish("only-role-a", "authorize role_a")))
                p.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            // 2. Scoped to any authenticated subscriber.
            using (var p = await clientA.PostAsync(PublishUrl, Publish("any-authenticated", "authorize")))
                p.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            // 3. Unscoped terminator: delivered to everyone (subscribe endpoint is anonymous, default scope).
            using (var p = await clientA.PostAsync(PublishUrl, Publish("everyone", null)))
                p.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            // A (role_a) receives all three, in order.
            (await sseA.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("only-role-a");
            (await sseA.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("any-authenticated");
            (await sseA.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("everyone");

            // B (role_b) must NOT get the role_a event: its first event is the authenticated one.
            (await sseB.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("any-authenticated",
                "an event hinted 'authorize role_a' must not be delivered to a subscriber without role_a");
            (await sseB.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("everyone");

            // Anonymous must get NEITHER scoped event: its first event is the unscoped one.
            (await sseAnon.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("everyone",
                "events hinted 'authorize …' must never be delivered to unauthenticated subscribers");
        }
    }
}
