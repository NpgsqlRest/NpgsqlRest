using Microsoft.Extensions.Logging;

namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void SseAnnotationTests()
        {
            // sset_* routines load only into SseAnnotationTestFixture (NameSimilarTo = "sset[_]%"); the
            // default fixture excludes them via NameNotSimilarTo. Each routine is annotated to exercise
            // a specific @sse / @sse_publish / @sse_subscribe combination plus the unbound case for
            // Phase A.
            script.Append("""

        -- Subscribe-only: exposes the EventSource URL but body never publishes.
        create function sset_subscribe_user_events()
        returns void
        language plpgsql immutable
        as $$ begin perform 1; end $$;
        comment on function sset_subscribe_user_events() is '
        HTTP GET
        sse_subscribe
        ';

        -- Publish-only: NO subscribe URL exposed; RAISE goes to the broadcaster.
        create procedure sset_publish_message(_msg text)
        language plpgsql
        as $$ begin raise info '%', _msg; end $$;
        comment on procedure sset_publish_message(text) is '
        HTTP POST
        sse_publish
        ';

        -- Shorthand: @sse means publish + subscribe on the same path (back-compat).
        create procedure sset_shorthand_emit(_msg text)
        language plpgsql
        as $$ begin raise info '%', _msg; end $$;
        comment on procedure sset_shorthand_emit(text) is '
        HTTP POST
        sse
        ';

        -- Unbound at INFO level: triggers the Phase A warning (RAISE INFO matches default SSE level).
        create procedure sset_unbound_emit(_msg text)
        language plpgsql
        as $$ begin raise info '%', _msg; end $$;
        comment on procedure sset_unbound_emit(text) is 'HTTP POST';

        -- Unbound at NOTICE level: must NOT trigger the warning — level doesn't match SSE INFO.
        create procedure sset_unbound_notice(_msg text)
        language plpgsql
        as $$ begin raise notice '%', _msg; end $$;
        comment on procedure sset_unbound_notice(text) is 'HTTP POST';
        """);
        }
    }
}

namespace NpgsqlRestTests.SseTests
{
    using NpgsqlRestTests.Setup;

    [Collection("SseAnnotationTestFixture")]
    public class SseAnnotationTests(SseAnnotationTestFixture test)
    {
        // ---------------------------------------------------------------------
        // Phase B.1 — URL gating: @sse_publish exposes no URL, @sse_subscribe does.
        // ---------------------------------------------------------------------

        [Fact]
        public async Task SsePublishOnly_HasNoSubscribeUrl()
        {
            // sset_publish_message is annotated @sse_publish. The procedure body publishes via RAISE
            // when POSTed to /api/sset-publish-message, but the /info subscribe URL must NOT exist —
            // that's the whole point of the publish/subscribe split.
            using var client = test.CreateClient();
            using var response = await client.GetAsync("/api/sset-publish-message/info");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "@sse_publish must not register a subscribe URL on the routine's path");
        }

        [Fact]
        public async Task SseSubscribeOnly_ExposesSubscribeUrl()
        {
            // sset_subscribe_user_events is annotated @sse_subscribe. The /info URL must exist and
            // serve text/event-stream. We open the connection just long enough to inspect the
            // response headers, then dispose — no events are published in this test.
            using var client = test.CreateClient();
            using var response = await client.GetAsync(
                "/api/sset-subscribe-user-events/info",
                HttpCompletionOption.ResponseHeadersRead);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        }

        [Fact]
        public async Task SseShorthand_BothBehaviors_BackCompat()
        {
            // sset_shorthand_emit uses @sse (no _publish/_subscribe split). The /info URL must exist
            // (subscribe behavior preserved) AND POST must work and publish to the broadcaster
            // (covered by the cross-procedure live test below; here we just check URL presence).
            using var client = test.CreateClient();
            using var response = await client.GetAsync(
                "/api/sset-shorthand-emit/info",
                HttpCompletionOption.ResponseHeadersRead);
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "@sse shorthand must continue to expose the /info subscribe URL (back-compat)");
        }

        // ---------------------------------------------------------------------
        // Phase B.1 — Cross-procedure live event flow.
        // Connect EventSource to a subscribe-only URL, POST to a publish-only procedure, assert the
        // RAISE INFO arrives on the open stream. This is the actual mathmodule pattern (subscribe via
        // one routine, publish via others) and the headline guarantee of the decomposition.
        // ---------------------------------------------------------------------

        [Fact]
        public async Task SsePublish_EventReachesSseSubscribeSubscriber()
        {
            using var client = test.CreateClient();
            await using var sse = await SseTestClient.OpenAsync(client, "/api/sset-subscribe-user-events/info");
            await sse.WaitForRegisteredAsync();

            using var publishResponse = await client.PostAsync(
                "/api/sset-publish-message",
                new StringContent("{\"msg\":\"hello-from-publish\"}", System.Text.Encoding.UTF8, "application/json"));
            publishResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            var data = await sse.ReadDataLineAsync(TimeSpan.FromSeconds(5));
            data.Should().Be("hello-from-publish",
                "RAISE INFO from a different (publish-only) procedure must reach subscribers connected via the subscribe-only URL — the cross-procedure pattern Phase B.1 is built to support");
        }

        [Fact]
        public async Task SseShorthand_PublishesToOwnSubscribers()
        {
            // The shorthand still publishes too — this verifies @sse keeps the publish half of its
            // historical behavior after Phase B.1. Open EventSource on the shorthand's own /info URL,
            // POST to the same path, observe the event.
            using var client = test.CreateClient();
            await using var sse = await SseTestClient.OpenAsync(client, "/api/sset-shorthand-emit/info");
            await sse.WaitForRegisteredAsync();

            using var publishResponse = await client.PostAsync(
                "/api/sset-shorthand-emit",
                new StringContent("{\"msg\":\"shorthand-still-publishes\"}", System.Text.Encoding.UTF8, "application/json"));
            publishResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            var data = await sse.ReadDataLineAsync(TimeSpan.FromSeconds(5));
            data.Should().Be("shorthand-still-publishes",
                "@sse shorthand must continue to publish from its own routine body (back-compat)");
        }

        // ---------------------------------------------------------------------
        // Phase A — unbound-RAISE warning.
        // ---------------------------------------------------------------------

        private static IEnumerable<LogEntry> WarningsFor(IEnumerable<LogEntry> logs, string path) =>
            logs.Where(e => e.Level == LogLevel.Warning
                && e.Message.Contains("was not broadcast to SSE subscribers")
                && e.Message.Contains(path));

        [Fact]
        public async Task RaiseInfo_NoSseAnnotation_LogsExactlyOneWarning()
        {
            // sset_unbound_emit has no @sse / @sse_publish, but its body does RAISE INFO. Because the
            // fixture has other endpoints with @sse_publish (sset_publish_message, sset_shorthand_emit),
            // HasAnySseEndpoints is true — so the warning fires for this unbound endpoint's first
            // RAISE INFO, naming the path so the developer can find the missing annotation.
            SseUnboundWarner.Reset();

            using var client = test.CreateClient();
            var before = test.CurrentLogCount;
            using var response = await client.PostAsync(
                "/api/sset-unbound-emit",
                new StringContent("{\"msg\":\"trigger-warn\"}", System.Text.Encoding.UTF8, "application/json"));
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            var matches = WarningsFor(test.LogsSince(before), "/api/sset-unbound-emit").ToList();
            matches.Should().ContainSingle();
            matches[0].Message.Should().Contain("INFO", "the warning must echo the severity that triggered it");
        }

        [Fact]
        public async Task RaiseInfo_NoSseAnnotation_TwiceSameEndpoint_StillOneWarning()
        {
            // Dedupe is per-endpoint per-process: a chatty unbound endpoint must not flood the log.
            SseUnboundWarner.Reset();

            using var client = test.CreateClient();
            var before = test.CurrentLogCount;
            for (int i = 0; i < 3; i++)
            {
                using var response = await client.PostAsync(
                    "/api/sset-unbound-emit",
                    new StringContent("{\"msg\":\"trigger-" + i + "\"}", System.Text.Encoding.UTF8, "application/json"));
                response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
            }

            var matches = WarningsFor(test.LogsSince(before), "/api/sset-unbound-emit").ToList();
            matches.Should().ContainSingle("the per-endpoint dedupe set must collapse repeats into a single warning");
        }

        [Fact]
        public async Task RaiseNotice_NoSseAnnotation_LevelDoesNotMatch_NoWarning()
        {
            // sset_unbound_notice does RAISE NOTICE, not INFO. The fixture's DefaultSseEventNoticeLevel
            // is INFO, so this notice would not be forwarded by SSE even with @sse_publish — and so
            // the warning must NOT fire (the developer is doing logging-via-RAISE, not SSE-via-RAISE).
            SseUnboundWarner.Reset();

            using var client = test.CreateClient();
            var before = test.CurrentLogCount;
            using var response = await client.PostAsync(
                "/api/sset-unbound-notice",
                new StringContent("{\"msg\":\"this-is-just-a-log\"}", System.Text.Encoding.UTF8, "application/json"));
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            var matches = WarningsFor(test.LogsSince(before), "/api/sset-unbound-notice").ToList();
            matches.Should().BeEmpty(
                "the warning must not fire for RAISE levels that the SSE forwarder wouldn't have broadcast anyway");
        }

        [Fact]
        public async Task RaiseInfo_WithSseShorthand_NoWarning()
        {
            // sset_shorthand_emit has @sse (publish+subscribe). Its RAISE INFO is broadcast normally;
            // the warning must not fire because the endpoint IS a publisher.
            SseUnboundWarner.Reset();

            using var client = test.CreateClient();
            var before = test.CurrentLogCount;
            using var response = await client.PostAsync(
                "/api/sset-shorthand-emit",
                new StringContent("{\"msg\":\"this-is-published\"}", System.Text.Encoding.UTF8, "application/json"));
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            var matches = WarningsFor(test.LogsSince(before), "/api/sset-shorthand-emit").ToList();
            matches.Should().BeEmpty("@sse routines must not trigger the unbound-RAISE warning");
        }

        [Fact]
        public async Task RaiseInfo_WithSsePublishAnnotation_NoWarning()
        {
            // sset_publish_message has @sse_publish. RAISE INFO is forwarded; no warning.
            SseUnboundWarner.Reset();

            using var client = test.CreateClient();
            var before = test.CurrentLogCount;
            using var response = await client.PostAsync(
                "/api/sset-publish-message",
                new StringContent("{\"msg\":\"explicit-publisher\"}", System.Text.Encoding.UTF8, "application/json"));
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            var matches = WarningsFor(test.LogsSince(before), "/api/sset-publish-message").ToList();
            matches.Should().BeEmpty("@sse_publish routines must not trigger the unbound-RAISE warning");
        }
    }
}
