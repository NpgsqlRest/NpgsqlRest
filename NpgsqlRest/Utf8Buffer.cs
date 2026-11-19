using System.Runtime.CompilerServices;
using System.Text;

namespace NpgsqlRest;

/// <summary>
/// A pooled, growable UTF-8 byte buffer: the sink that response rows are rendered into.
/// PostgreSQL sends values as UTF-8 text and the HTTP response is UTF-8, so rows are built as bytes directly - a
/// value is never decoded into a UTF-16 string and encoded back.
/// Not thread-safe; rent one per operation and return it.
/// </summary>
internal sealed class Utf8Buffer
{
    private const int DefaultCapacity = 1024;
    // Buffers that grew beyond this are not kept in the pool (one huge response must not pin memory forever).
    private const int MaxRetainedCapacity = 256 * 1024;
    private const int MaxPoolSize = 64;

    private static readonly Utf8Buffer?[] Pool = new Utf8Buffer?[MaxPoolSize];
    private static int _poolIndex = -1; // index of the top occupied slot

    private byte[] _buffer;
    private int _length;

    private Utf8Buffer(int capacity) => _buffer = GC.AllocateUninitializedArray<byte>(capacity);

    /// <summary>Rents an empty buffer from the pool (or creates one).</summary>
    public static Utf8Buffer Rent(int minimumCapacity = DefaultCapacity)
    {
        Utf8Buffer? buffer = null;
        int index = Interlocked.Decrement(ref _poolIndex) + 1; // the slot that was on top before the decrement
        if (index >= 0 && index < MaxPoolSize)
        {
            buffer = Interlocked.Exchange(ref Pool[index], null);
        }
        else
        {
            Interlocked.Increment(ref _poolIndex);
        }
        if (buffer is null)
        {
            return new Utf8Buffer(Math.Max(minimumCapacity, DefaultCapacity));
        }
        if (buffer._buffer.Length < minimumCapacity)
        {
            buffer._buffer = GC.AllocateUninitializedArray<byte>(minimumCapacity);
        }
        return buffer;
    }

    /// <summary>Returns a buffer to the pool. The buffer must not be used afterwards.</summary>
    public static void Return(Utf8Buffer buffer)
    {
        buffer._length = 0;
        if (buffer._buffer.Length > MaxRetainedCapacity)
        {
            return;
        }
        int index = Interlocked.Increment(ref _poolIndex);
        if (index >= 0 && index < MaxPoolSize)
        {
            Pool[index] = buffer;
        }
        else
        {
            Interlocked.Decrement(ref _poolIndex);
        }
    }

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    public ReadOnlySpan<byte> WrittenSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(_buffer, 0, _length);
    }

    public ReadOnlyMemory<byte> WrittenMemory => new(_buffer, 0, _length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear() => _length = 0;

    /// <summary>Returns writable space of at least <paramref name="sizeHint"/> bytes after the written data.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint)
    {
        if (_buffer.Length - _length < sizeHint)
        {
            Grow(sizeHint);
        }
        return _buffer.AsSpan(_length);
    }

    /// <summary>Marks <paramref name="count"/> bytes of the span returned by <see cref="GetSpan"/> as written.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count) => _length += count;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int sizeHint)
    {
        long required = (long)_length + sizeHint;
        long doubled = (long)_buffer.Length * 2;
        long newSize = Math.Max(required, doubled);
        if (newSize > Array.MaxLength)
        {
            newSize = required > Array.MaxLength ? throw new OutOfMemoryException("The response row is too large to buffer.") : Array.MaxLength;
        }
        var newBuffer = GC.AllocateUninitializedArray<byte>((int)newSize);
        _buffer.AsSpan(0, _length).CopyTo(newBuffer);
        _buffer = newBuffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(byte value)
    {
        if (_length == _buffer.Length)
        {
            Grow(1);
        }
        _buffer[_length++] = value;
    }

    /// <summary>Appends one UTF-16 code unit; the structural JSON characters this is used for are all ASCII.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(char value)
    {
        if (value < 0x80)
        {
            Append((byte)value);
        }
        else
        {
            Append(new ReadOnlySpan<char>(in value));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(ReadOnlySpan<byte> utf8)
    {
        utf8.CopyTo(GetSpan(utf8.Length));
        _length += utf8.Length;
    }

    public void Append(string? value)
    {
        if (value is not null)
        {
            Append(value.AsSpan());
        }
    }

    /// <summary>Appends UTF-16 text, encoded as UTF-8.</summary>
    public void Append(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return;
        }
        // Small values: reserve the worst case (3 bytes per char) and encode once. Large values: count first, so a big
        // ASCII value does not triple the buffer.
        int reserve = value.Length <= 256 ? value.Length * 3 : Encoding.UTF8.GetByteCount(value);
        _length += Encoding.UTF8.GetBytes(value, GetSpan(reserve));
    }

    public void Append(Utf8Buffer other) => Append(other.WrittenSpan);

    public override string ToString() => Encoding.UTF8.GetString(WrittenSpan);
}
