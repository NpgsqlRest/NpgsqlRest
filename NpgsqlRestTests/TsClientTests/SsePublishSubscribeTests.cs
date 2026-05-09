namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientSsePublishSubscribeTests()
        {
            // Two routines that exercise the @sse_publish / @sse_subscribe split as the TS client
            // generator sees it. The publish-only routine has no subscribe URL — the generated TS
            // must therefore have NO createXEventSource helper and NO SSE wrapper logic. The
            // subscribe-only routine has a URL — the generated TS must include the EventSource
            // helper just like the existing @sse shorthand case.
            script.Append("""

create function tsclient_test.sse_publish_only(_message text)
returns text
language plpgsql
as $$
begin
    raise info '%', _message;
    return 'published';
end;
$$;
comment on function tsclient_test.sse_publish_only(text) is '
tsclient_module=sse_publish_only
HTTP POST
sse_publish
';

create function tsclient_test.sse_subscribe_only()
returns void
language plpgsql immutable
as $$ begin perform 1; end $$;
comment on function tsclient_test.sse_subscribe_only() is '
tsclient_module=sse_subscribe_only
HTTP GET
sse_subscribe /api/tsclient-test/sse-subscribe-only/events
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    /// <summary>
    /// Verifies the TS client generator's behavior under the new <c>@sse_publish</c> /
    /// <c>@sse_subscribe</c> annotations. We assert the binary feature presence/absence
    /// (EventSource helper, SSE-wrapper params) rather than exact byte match — the existing
    /// <see cref="SseTests"/> already covers full-file shape for the <c>@sse</c> shorthand path.
    /// </summary>
    [Collection("TestFixture")]
    public class SsePublishSubscribeTests
    {
        [Fact]
        public void TsClient_SsePublishOnly_OmitsEventSourceHelper()
        {
            // @sse_publish exposes no subscribe URL, so the TS client must generate a plain HTTP
            // function with no SSE machinery: no createXEventSource helper, no onMessage callback,
            // no execution-id wiring, no awaitConnectionMs/closeAfterMs parameters.
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "sse_publish_only.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            content.Should().NotContain("EventSource",
                "publish-only routines have no subscribe URL — emitting an EventSource helper would let consumers connect to a 404");
            content.Should().NotContain("onMessage",
                "publish-only functions take no SSE callback");
            content.Should().NotContain("X-NpgsqlRest-ID",
                "publish-only functions don't correlate with an SSE connection, so the execution-id header is not set");

            content.Should().Contain("export async function tsclientTestSsePublishOnly",
                "the plain HTTP function for the publisher must still be generated");
            content.Should().Contain("\"/api/tsclient-test/sse-publish-only\"",
                "the HTTP function must POST to the routine's main path (not the /info SSE suffix)");
            content.Should().Contain("method: \"POST\"",
                "publish-only routines keep their original HTTP method");
        }

        [Fact]
        public void TsClient_SseSubscribeOnly_GeneratesEventSourceHelper()
        {
            // @sse_subscribe registers a subscribe URL, so the TS client continues to emit the
            // createXEventSource helper plus the SSE-aware function wrapper — same shape as today's
            // @sse shorthand. The SSE wrapper still works against the routine's main path even if
            // the body is a no-op (typical mathmodule-style "subscribe-only stub" pattern).
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "sse_subscribe_only.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            content.Should().Contain(
                "export const createTsclientTestSseSubscribeOnlyEventSource = (id: string = \"\") => new EventSource(baseUrl + \"/api/tsclient-test/sse-subscribe-only/events?\" + id);",
                "subscribe-only routines must emit the EventSource helper bound to the @sse_subscribe path");
            content.Should().Contain("onMessage?: (message: string) => void",
                "the SSE-aware function wrapper must accept an optional onMessage callback");
            content.Should().Contain("X-NpgsqlRest-ID",
                "the SSE-aware function wrapper must set the execution-id header so the connection can correlate per-request events");
        }
    }
}
