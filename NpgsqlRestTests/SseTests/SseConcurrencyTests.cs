namespace NpgsqlRestTests.SseTests;

using NpgsqlRestTests.Setup;

/// <summary>
/// Concurrency and lifecycle coverage for the SSE broadcaster: multiple simultaneous subscribers,
/// exactly-once delivery per subscriber, per-stream ordering, and subscriber disconnect not
/// affecting the remaining streams. Reuses the sset_* routines from <see cref="SseAnnotationTests"/>
/// (same fixture/collection, so tests run sequentially against one broadcaster).
/// </summary>
[Collection("SseAnnotationTestFixture")]
public class SseConcurrencyTests(SseAnnotationTestFixture test)
{
    private const string SubscribeUrl = "/api/sset-subscribe-user-events/info";
    private const string PublishUrl = "/api/sset-publish-message";

    private static StringContent Message(string msg) =>
        new($"{{\"msg\":\"{msg}\"}}", System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Multiple_Concurrent_Subscribers_Each_Receive_The_Event_Exactly_Once()
    {
        using var client = test.CreateClient();

        const int subscriberCount = 5;
        var subscribers = new List<SseTestClient>();
        try
        {
            for (var i = 0; i < subscriberCount; i++)
            {
                subscribers.Add(await SseTestClient.OpenAsync(client, SubscribeUrl));
            }
            foreach (var s in subscribers)
            {
                await s.WaitForRegisteredAsync();
            }

            using var publish = await client.PostAsync(PublishUrl, Message("fanout-1"));
            publish.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            // Every subscriber gets the event...
            foreach (var s in subscribers)
            {
                var data = await s.ReadDataLineAsync(TimeSpan.FromSeconds(10));
                data.Should().Be("fanout-1", "every concurrent subscriber must receive the broadcast");
            }

            // ...exactly once: the next data line each subscriber sees is the NEXT event, not a duplicate.
            using var publish2 = await client.PostAsync(PublishUrl, Message("fanout-2"));
            publish2.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            foreach (var s in subscribers)
            {
                var data = await s.ReadDataLineAsync(TimeSpan.FromSeconds(10));
                data.Should().Be("fanout-2", "no duplication: the second read must be the second event");
            }
        }
        finally
        {
            foreach (var s in subscribers)
            {
                await s.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Events_Arrive_In_Publish_Order_On_Each_Stream()
    {
        using var client = test.CreateClient();
        await using var sse = await SseTestClient.OpenAsync(client, SubscribeUrl);
        await sse.WaitForRegisteredAsync();

        // Publish sequentially (each POST completes before the next starts) - per-stream
        // delivery order must match publish order.
        for (var i = 0; i < 5; i++)
        {
            using var publish = await client.PostAsync(PublishUrl, Message($"ordered-{i}"));
            publish.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        }

        for (var i = 0; i < 5; i++)
        {
            var data = await sse.ReadDataLineAsync(TimeSpan.FromSeconds(10));
            data.Should().Be($"ordered-{i}", "events must arrive in publish order on a single stream");
        }
    }

    [Fact]
    public async Task Disconnected_Subscriber_Does_Not_Affect_Remaining_Subscribers()
    {
        using var client = test.CreateClient();

        var early = await SseTestClient.OpenAsync(client, SubscribeUrl);
        await early.WaitForRegisteredAsync();
        await using var survivor = await SseTestClient.OpenAsync(client, SubscribeUrl);
        await survivor.WaitForRegisteredAsync();

        // Both receive the first event.
        using (var publish = await client.PostAsync(PublishUrl, Message("before-disconnect")))
        {
            publish.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        }
        (await early.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("before-disconnect");
        (await survivor.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("before-disconnect");

        // Drop one subscriber, then publish twice more - the survivor must keep receiving,
        // and publishing must not error against the dead connection.
        await early.DisposeAsync();

        using (var publish = await client.PostAsync(PublishUrl, Message("after-disconnect-1")))
        {
            publish.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        }
        (await survivor.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("after-disconnect-1");

        using (var publish = await client.PostAsync(PublishUrl, Message("after-disconnect-2")))
        {
            publish.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        }
        (await survivor.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("after-disconnect-2");

        // A fresh subscriber after the disconnect still works (broadcaster registry stays healthy).
        await using var late = await SseTestClient.OpenAsync(client, SubscribeUrl);
        await late.WaitForRegisteredAsync();
        using (var publish = await client.PostAsync(PublishUrl, Message("late-joiner")))
        {
            publish.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        }
        (await late.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("late-joiner");
        (await survivor.ReadDataLineAsync(TimeSpan.FromSeconds(10))).Should().Be("late-joiner");
    }
}
