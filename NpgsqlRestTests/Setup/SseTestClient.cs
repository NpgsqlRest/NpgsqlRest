using System.Net.Http;
using NpgsqlRest;

namespace NpgsqlRestTests.Setup;

/// <summary>
/// Streaming-HTTP helper for live SSE flow tests. Opens an EventSource-style GET against the
/// test server, drains the response body line-by-line, and exposes one method to read the next
/// <c>data:</c> line with a timeout. The class disposes cleanly even when the underlying stream
/// is still being held open by the server-side <c>await reader.ReadAllAsync</c> loop.
///
/// Usage shape:
/// <code>
///   await using var sse = await SseTestClient.OpenAsync(client, "/api/x/info");
///   await sse.WaitForRegisteredAsync();   // server-side Subscribe() observed
///   // ... POST to publisher endpoint ...
///   string? data = await sse.ReadDataLineAsync(TimeSpan.FromSeconds(5));
/// </code>
/// The TestServer transport is in-memory, so timing is fast and deterministic; tolerances below
/// are generous to keep CI machines happy without hiding real regressions.
/// </summary>
public sealed class SseTestClient : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly CancellationTokenSource _cts;
    private readonly int _expectedSubscriberCount;

    private SseTestClient(HttpResponseMessage response, Stream stream, StreamReader reader, CancellationTokenSource cts, int expectedSubscriberCount)
    {
        _response = response;
        _stream = stream;
        _reader = reader;
        _cts = cts;
        _expectedSubscriberCount = expectedSubscriberCount;
    }

    /// <summary>
    /// Opens an SSE connection to <paramref name="url"/>. Returns once response headers arrive —
    /// the server may or may not have completed the <c>Broadcaster.Subscribe</c> call yet, so
    /// callers should follow up with <see cref="WaitForRegisteredAsync"/> before publishing.
    /// </summary>
    public static async Task<SseTestClient> OpenAsync(HttpClient client, string url, CancellationToken cancellationToken = default)
    {
        // Snapshot the broadcaster count BEFORE the GET so WaitForRegisteredAsync can detect the
        // increment cleanly, even when other tests in the same fixture leave subscribers around
        // (they shouldn't — Unsubscribe runs in finally — but defensive snapshotting costs nothing).
        var baseline = NpgsqlRestSseEventSource.Broadcaster.SubscriberCount;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var reader = new StreamReader(stream);
        return new SseTestClient(response, stream, reader, cts, baseline + 1);
    }

    /// <summary>
    /// Spins until the broadcaster has at least the expected number of subscribers (the count
    /// captured at <see cref="OpenAsync"/>). Avoids the race between "headers arrived at the
    /// client" and "<c>Subscribe</c> ran on the server." Throws on timeout.
    /// </summary>
    public async Task WaitForRegisteredAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (NpgsqlRestSseEventSource.Broadcaster.SubscriberCount < _expectedSubscriberCount)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"SSE subscriber did not register within timeout. Expected count >= {_expectedSubscriberCount}, observed {NpgsqlRestSseEventSource.Broadcaster.SubscriberCount}.");
            }
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Reads lines until one starts with <c>data:</c> and returns the value after the colon
    /// (trimmed of leading whitespace). Returns null if the stream ends before a data line is
    /// received within the timeout.
    /// </summary>
    public async Task<string?> ReadDataLineAsync(TimeSpan timeout)
    {
        using var perRead = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        perRead.CancelAfter(timeout);
        try
        {
            string? line;
            while ((line = await _reader.ReadLineAsync(perRead.Token)) is not null)
            {
                // SSE wire format: lines starting with "data:" carry the message body; blank lines
                // terminate an event; other lines (id:, event:, comment :keepalive) are framing.
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    return line[5..].TrimStart();
                }
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _reader.Dispose();
        await _stream.DisposeAsync();
        _response.Dispose();
        _cts.Dispose();
    }
}
