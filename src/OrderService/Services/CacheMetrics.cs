using System.Threading;

namespace OrderService.Services;

/// <summary>
/// Simple thread-safe counters for cache hits and misses.
/// </summary>
public class CacheMetrics
{
    /// <summary>
    /// Increment the cache hit counter by one.
    /// </summary>
    private long _hits;
    private long _misses;

    /// <summary>
    /// Record a cache hit.
    /// </summary>
    public void IncrementHit() => Interlocked.Increment(ref _hits);

    /// <summary>
    /// Record a cache miss.
    /// </summary>
    public void IncrementMiss() => Interlocked.Increment(ref _misses);

    /// <summary>
    /// Total number of cache hits recorded by this process.
    /// </summary>
    public long Hits => Interlocked.Read(ref _hits);

    /// <summary>
    /// Total number of cache misses recorded by this process.
    /// </summary>
    public long Misses => Interlocked.Read(ref _misses);
}
