namespace NpgsqlRestTests.SseTests;

using System.Text.Json;
using NpgsqlRestTests.Setup;

/// <summary>
/// Security regression: an SSE message containing line breaks is sent as one data: line per line. It used to be
/// written as-is, so a RAISE message carrying user text could add event:/id:/retry: fields or end the event early and
/// forge a second event in every subscriber's stream.
/// </summary>
[Collection("SseAnnotationTestFixture")]
public class SseEventFramingTests(SseAnnotationTestFixture test)
{
    [Fact]
    public async Task Line_breaks_in_a_message_cannot_inject_fields_or_events()
    {
        using var client = test.CreateClient();
        await using var sse = await SseTestClient.OpenAsync(client, "/api/sset-subscribe-user-events/info");
        await sse.WaitForRegisteredAsync();

        var message = "hello\nevent: admin\r\nretry: 1\n\ndata: forged";
        using var publish = await client.PostAsync("/api/sset-publish-message",
            new StringContent(JsonSerializer.Serialize(new Dictionary<string, string> { ["msg"] = message }), Encoding.UTF8, "application/json"));
        publish.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var lines = await sse.ReadEventLinesAsync(TimeSpan.FromSeconds(10));
        lines.Should().Equal(
            "data: hello",
            "data: event: admin",
            "data: retry: 1",
            "data: ",
            "data: data: forged");
    }
}

public class SseFramingUnitTests
{
    [Theory]
    [InlineData("plain", "data: plain\n\n")]
    [InlineData("", "data: \n\n")]
    [InlineData(null, "data: \n\n")]
    [InlineData("a\nb", "data: a\ndata: b\n\n")]
    [InlineData("a\r\nb", "data: a\ndata: b\n\n")]
    [InlineData("a\rb", "data: a\ndata: b\n\n")]
    [InlineData("a\n\nb\n", "data: a\ndata: \ndata: b\ndata: \n\n")]
    public void Every_line_becomes_its_own_data_line(string? message, string expected)
    {
        NpgsqlRestSseEventSource.FormatEventData(message).Should().Be(expected);
    }

    [Fact]
    public async Task Subscriber_that_does_not_read_keeps_only_the_newest_messages()
    {
        var broadcaster = new Broadcaster<int>(subscriberCapacity: 3);
        var id = Guid.NewGuid();
        var reader = broadcaster.Subscribe(id);
        for (var i = 1; i <= 5; i++)
        {
            broadcaster.Broadcast(i);
        }
        broadcaster.SubscriberCount.Should().Be(1, "a full buffer drops old messages, it does not drop the subscriber");

        var received = new List<int>();
        while (reader.TryRead(out var item))
        {
            received.Add(item);
        }
        received.Should().Equal(3, 4, 5);

        broadcaster.Broadcast(6);
        (await reader.ReadAsync()).Should().Be(6);
        broadcaster.Unsubscribe(id);
    }
}
