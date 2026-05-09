using System.Collections.Concurrent;
using System.Threading.Channels;

namespace NpgsqlRest;

public class Broadcaster<T>
{
    private readonly ConcurrentDictionary<Guid, Channel<T>> _channels = new();

    public void Broadcast(T message)
    {
        foreach (var kvp in _channels)
        {
            var writer = kvp.Value.Writer;
            if (!writer.TryWrite(message))
            {
                // Channel is closed, remove it
                _channels.TryRemove(kvp.Key, out _);
            }
        }
    }

    public ChannelReader<T> Subscribe(Guid subscriberId)
    {
        if (_channels.TryRemove(subscriberId, out var existingChannel))
        {
            existingChannel.Writer.TryComplete();
        }
        var channel = Channel.CreateUnbounded<T>();
        _channels[subscriberId] = channel;
        return channel.Reader;
    }

    public void Unsubscribe(Guid subscriberId)
    {
        if (_channels.TryRemove(subscriberId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public void CompleteAll()
    {
        foreach (var kvp in _channels)
        {
            kvp.Value.Writer.TryComplete();
        }
        _channels.Clear();
    }

    /// <summary>
    /// Number of currently subscribed channels. Used by integration tests to wait until an SSE
    /// subscriber has registered before triggering a publish, avoiding a race between
    /// <c>Subscribe</c> and the test's HTTP call. Cheap on a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
    /// </summary>
    public int SubscriberCount => _channels.Count;
}
