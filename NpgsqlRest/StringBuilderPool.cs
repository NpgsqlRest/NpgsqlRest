using System.Text;

namespace NpgsqlRest;

/// <summary>
/// A simple thread-safe StringBuilder pool to reduce allocations in high-throughput scenarios.
/// Uses a lock-free stack-based approach for fast rent/return operations.
/// </summary>
internal static class StringBuilderPool
{
    private const int MaxPoolSize = 64;
    private const int DefaultCapacity = 256;
    private const int MaxCapacity = 8192;

    private static readonly StringBuilder?[] _pool = new StringBuilder?[MaxPoolSize];
    private static int _index = -1;

    /// <summary>
    /// Rents a StringBuilder from the pool. Returns a new instance if the pool is empty.
    /// </summary>
    public static StringBuilder Rent()
    {
        StringBuilder? sb = null;
        int currentIndex = Interlocked.Decrement(ref _index);

        if (currentIndex >= 0 && currentIndex < MaxPoolSize)
        {
            sb = Interlocked.Exchange(ref _pool[currentIndex], null);
        }
        else
        {
            // Pool was empty or index went negative, restore it
            Interlocked.Increment(ref _index);
        }

        return sb ?? new StringBuilder(DefaultCapacity);
    }

    /// <summary>
    /// Rents a StringBuilder from the pool with a minimum capacity.
    /// </summary>
    public static StringBuilder Rent(int minimumCapacity)
    {
        var sb = Rent();
        if (sb.Capacity < minimumCapacity)
        {
            sb.EnsureCapacity(minimumCapacity);
        }
        return sb;
    }

    /// <summary>
    /// Returns a StringBuilder to the pool. The StringBuilder is cleared before being pooled.
    /// Large StringBuilders (capacity > MaxCapacity) are not pooled to avoid memory bloat.
    /// </summary>
    public static void Return(StringBuilder sb)
    {
        if (sb.Capacity > MaxCapacity)
        {
            // Don't pool oversized builders - let GC collect them
            return;
        }

        sb.Clear();

        int currentIndex = Interlocked.Increment(ref _index);
        if (currentIndex >= 0 && currentIndex < MaxPoolSize)
        {
            _pool[currentIndex] = sb;
        }
        else
        {
            // Pool is full, decrement index back and let the StringBuilder be collected
            Interlocked.Decrement(ref _index);
        }
    }

    /// <summary>
    /// Gets the string value and returns the StringBuilder to the pool.
    /// </summary>
    public static string ToStringAndReturn(StringBuilder sb)
    {
        var result = sb.ToString();
        Return(sb);
        return result;
    }
}
