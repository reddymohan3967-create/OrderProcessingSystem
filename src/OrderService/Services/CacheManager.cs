using System;
using System.Threading;

namespace OrderService.Services;

/// <summary>
/// Runtime-manageable cache configuration (TTL values) that can be modified
/// by admin endpoints. Thread-safe.
/// </summary>
public class CacheManager
{
    private int _orderTtlSeconds;
    private int _ordersListTtlSeconds;

    /// <summary>
    /// Initializes a new instance of <see cref="CacheManager"/> with the provided default TTLs.
    /// </summary>
    /// <param name="orderTtlSeconds">Default TTL, in seconds, for single-order cache entries. Must be &gt;= 1.</param>
    /// <param name="ordersListTtlSeconds">Default TTL, in seconds, for order-list cache entries. Must be &gt;= 1.</param>
    public CacheManager(int orderTtlSeconds = 30, int ordersListTtlSeconds = 15)
    {
        _orderTtlSeconds = Math.Max(1, orderTtlSeconds);
        _ordersListTtlSeconds = Math.Max(1, ordersListTtlSeconds);
    }
    /// <summary>
    /// Gets the current TTL used for single-order cache entries as a <see cref="TimeSpan"/>.
    /// </summary>
    public TimeSpan OrderTtl => TimeSpan.FromSeconds(Volatile.Read(ref _orderTtlSeconds));

    /// <summary>
    /// Gets the current TTL used for order-list cache entries as a <see cref="TimeSpan"/>.
    /// </summary>
    public TimeSpan OrdersListTtl => TimeSpan.FromSeconds(Volatile.Read(ref _ordersListTtlSeconds));

    /// <summary>
    /// Returns the current single-order TTL in seconds.
    /// </summary>
    public int GetOrderTtlSeconds() => Volatile.Read(ref _orderTtlSeconds);

    /// <summary>
    /// Returns the current order-list TTL in seconds.
    /// </summary>
    public int GetOrdersListTtlSeconds() => Volatile.Read(ref _ordersListTtlSeconds);

    /// <summary>
    /// Set the TTL (in seconds) used for single-order cache entries. Value must be &gt;= 1.
    /// Changes take effect immediately for subsequent cache writes.
    /// </summary>
    /// <param name="seconds">TTL in seconds.</param>
    public void SetOrderTtlSeconds(int seconds)
    {
        if (seconds < 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        Volatile.Write(ref _orderTtlSeconds, seconds);
    }

    /// <summary>
    /// Set the TTL (in seconds) used for order-list cache entries. Value must be &gt;= 1.
    /// Changes take effect immediately for subsequent cache writes.
    /// </summary>
    /// <param name="seconds">TTL in seconds.</param>
    public void SetOrdersListTtlSeconds(int seconds)
    {
        if (seconds < 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        Volatile.Write(ref _ordersListTtlSeconds, seconds);
    }
}
