using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using OrderService.Services;

namespace OrderService.Controllers;

[ApiController]
[Authorize(Policy = "RequireAdmin")]
[Route("api/admin/cache")]
/// <summary>
/// Administrative endpoints for inspecting and managing cache behaviour at runtime.
/// Protected by the "RequireAdmin" policy. Provides read and update operations
/// for cache TTLs and simple hit/miss metrics.
/// </summary>
public class AdminCacheController : ControllerBase
{
    private readonly CacheManager _manager;
    private readonly CacheMetrics _metrics;

    public AdminCacheController(CacheManager manager, CacheMetrics metrics)
    {
        _manager = manager;
        _metrics = metrics;
    }

    /// <summary>
    /// Returns the current cache TTLs used by the application for single-order
    /// entries and order list entries (seconds).
    /// </summary>
    [HttpGet("ttls")]
    public IActionResult GetTtls()
    {
        return Ok(new
        {
            OrderTtlSeconds = _manager.GetOrderTtlSeconds(),
            OrdersListTtlSeconds = _manager.GetOrdersListTtlSeconds()
        });
    }

    /// <summary>
    /// Update cache TTLs at runtime. Provide one or both fields in the request body.
    /// Values are applied immediately and affect subsequent cache writes. Values
    /// must be positive integers representing seconds.
    /// </summary>
    /// <param name="req">Request containing optional TTL values in seconds.</param>
    [HttpPost("ttls")]
    public IActionResult SetTtls([FromBody] SetTtlsRequest req)
    {
        if (req.OrderTtlSeconds.HasValue) _manager.SetOrderTtlSeconds(req.OrderTtlSeconds.Value);
        if (req.OrdersListTtlSeconds.HasValue) _manager.SetOrdersListTtlSeconds(req.OrdersListTtlSeconds.Value);

        return NoContent();
    }

    /// <summary>
    /// Returns simple cache metrics for this process (hit and miss counters).
    /// </summary>
    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        return Ok(new
        {
            CacheHits = _metrics.Hits,
            CacheMisses = _metrics.Misses
        });
    }

    /// <summary>
    /// Request model for updating cache TTLs via the admin API.
    /// </summary>
    public class SetTtlsRequest
    {
        /// <summary>
        /// Time-to-live for single order cache entries, in seconds.
        /// </summary>
        public int? OrderTtlSeconds { get; set; }

        /// <summary>
        /// Time-to-live for order list cache entries, in seconds.
        /// </summary>
        public int? OrdersListTtlSeconds { get; set; }
    }
}
