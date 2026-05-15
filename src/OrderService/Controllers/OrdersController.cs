using Microsoft.AspNetCore.Mvc;
using OrderService.Interfaces;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.DTOs;
using Shared.Contracts.Enums;

namespace OrderService.Controllers;

[ApiController]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("PerIpPolicy")]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;
    private readonly OrderService.Data.AppDbContext _db;

    public OrdersController(IOrderService orderService, ILogger<OrdersController> logger, OrderService.Data.AppDbContext db)
    {
        _orderService = orderService;
        _logger = logger;
        _db = db;
    }

    /// <summary>
    /// Creates a new order.
    /// </summary>
    /// <param name="request">Order creation request DTO.</param>
    /// <returns>Created order response with location header.</returns>

    private static Guid? ParseOrderId(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("OrderId", out var p))
            {
                if (p.ValueKind == System.Text.Json.JsonValueKind.String && Guid.TryParse(p.GetString(), out var g))
                    return g;
            }
        }
        catch { }
        return null;
    }

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> CreateOrder(CreateOrderRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _orderService.CreateOrderAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<ActionResult<OrderResponse>> GetById(Guid id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);

        if (order is null)
            return NotFound();

        // If requester is a customer, ensure they only access their own orders
        if (User.IsInRole("Customer"))
        {
            var userEmail = User.Identity?.Name ?? string.Empty;
            if (!string.Equals(order.Email ?? string.Empty, userEmail, StringComparison.OrdinalIgnoreCase))
                return Forbid();
        }

        return Ok(order);
    }

    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<ActionResult<List<OrderResponse>>> GetAll([FromQuery] OrderStatus? status)
    {
        try
        {
            _logger.LogInformation("GetAll invoked. User: {user}, IsAuthenticated: {auth}", User.Identity?.Name, User.Identity?.IsAuthenticated);

            foreach (var c in User.Claims)
            {
                _logger.LogDebug("Claim: {type} = {value}", c.Type, c.Value);
            }

            // Role-based filtering:
            // - Admin: can see all orders (honors optional status filter)
            // - Customer: only Pending orders (ignore status query)
            // - ShippingAdmin: only Processing orders
            // - DeliveryAdmin: only Shipping orders

            List<OrderResponse> orders;

            if (User.IsInRole("Admin"))
            {
                orders = await _orderService.GetOrdersAsync(status);
            }
            else if (User.IsInRole("Customer"))
            {
                // Customers see only their own orders. If a status query is provided, honor it; otherwise return all orders for the user.
                var userEmail = User.Identity?.Name ?? string.Empty;
                if (status.HasValue)
                    orders = await _orderService.GetOrdersAsync(status);
                else
                    orders = await _orderService.GetOrdersAsync(null);

                orders = orders.Where(o => string.Equals(o.Email ?? string.Empty, userEmail, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else if (User.IsInRole("ShippingAdmin"))
            {
                orders = await _orderService.GetOrdersAsync(Shared.Contracts.Enums.OrderStatus.Processing);
            }
            else if (User.IsInRole("DeliveryAdmin"))
            {
                orders = await _orderService.GetOrdersAsync(Shared.Contracts.Enums.OrderStatus.Shipped);
            }
            else
            {
                // Not in any recognized role - deny access
                return Forbid();
            }

            return Ok(orders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get orders");
            // Return full exception for easier debugging in development.
            return StatusCode(500, new { error = ex.Message, detail = ex.ToString() });
        }
    }

    [HttpGet("cancelled")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> GetCancelled()
    {
        var orders = await _orderService.GetOrdersAsync(OrderStatus.Cancelled);
        return Ok(orders);
    }

    [HttpDelete("{id:guid}")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Cancel(Guid id)
    {
        try
        {
            // Admins can cancel any order (force)
            if (User.IsInRole("Admin"))
            {
                var ok = await _orderService.UpdateOrderStatusAsync(id, OrderStatus.Cancelled, true);
                if (!ok) return NotFound();
                return NoContent();
            }

            // For non-admins (customers), ensure ownership before attempting cancellation
            if (User.IsInRole("Customer"))
            {
                var order = await _orderService.GetOrderByIdAsync(id);
                if (order is null) return NotFound();
                var userEmail = User.Identity?.Name ?? string.Empty;
                if (!string.Equals(order.Email ?? string.Empty, userEmail, StringComparison.OrdinalIgnoreCase))
                    return Forbid();
            }

            var cancelled = await _orderService.CancelOrderAsync(id);

            if (!cancelled)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/ship")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> MarkShipped(Guid id)
    {
        // only ShippingAdmin and Admin
        if (!User.IsInRole("ShippingAdmin") && !User.IsInRole("Admin"))
            return Forbid();

        try
        {
            // capture existing state so we can ensure an outbox entry exists
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order is null) return NotFound();

            var oldStatus = order.Status;

            var force = User.IsInRole("Admin");
            var ok = await _orderService.UpdateOrderStatusAsync(id, OrderStatus.Shipped, force);
            if (!ok) return NotFound();

            // Ensure an OrderStatusUpdatedEvent outbox exists for this change. In some flows
            // the status update path may not have created an outbox entry (legacy callers),
            // so create one if missing.
            // More robust check: load recent outbox messages for this event type and parse JSON payloads
            // to find a payload with OrderId == id. This avoids brittle string matching.
            var candidates = await _db.OutboxMessages
                .Where(m => m.EventType == nameof(Shared.Contracts.Events.OrderStatusUpdatedEvent))
                .OrderByDescending(m => m.CreatedAtUtc)
                .Take(50)
                .ToListAsync();

            var exists = candidates.Any(m => ParseOrderId(m.Payload) == id);

            if (!exists)
            {
                var evt = new Shared.Contracts.Events.OrderStatusUpdatedEvent
                {
                    OrderId = id,
                    OldStatus = oldStatus,
                    NewStatus = OrderStatus.Shipped,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Email = order.Email ?? string.Empty
                };

                _db.OutboxMessages.Add(new OrderService.Entities.OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = nameof(Shared.Contracts.Events.OrderStatusUpdatedEvent),
                    Payload = System.Text.Json.JsonSerializer.Serialize(evt),
                    CreatedAtUtc = DateTime.UtcNow,
                    RetryCount = 0
                });

                await _db.SaveChangesAsync();
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/deliver")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> MarkDelivered(Guid id)
    {
        // only DeliveryAdmin and Admin
        if (!User.IsInRole("DeliveryAdmin") && !User.IsInRole("Admin"))
            return Forbid();

        try
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order is null) return NotFound();

            var oldStatus = order.Status;

            var force = User.IsInRole("Admin");
            var ok = await _orderService.UpdateOrderStatusAsync(id, OrderStatus.Delivered, force);
            if (!ok) return NotFound();

            var candidates2 = await _db.OutboxMessages
                .Where(m => m.EventType == nameof(Shared.Contracts.Events.OrderStatusUpdatedEvent))
                .OrderByDescending(m => m.CreatedAtUtc)
                .Take(50)
                .ToListAsync();

            var exists = candidates2.Any(m => ParseOrderId(m.Payload) == id);

            if (!exists)
            {
                var evt = new Shared.Contracts.Events.OrderStatusUpdatedEvent
                {
                    OrderId = id,
                    OldStatus = oldStatus,
                    NewStatus = OrderStatus.Delivered,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Email = order.Email ?? string.Empty
                };

                _db.OutboxMessages.Add(new OrderService.Entities.OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = nameof(Shared.Contracts.Events.OrderStatusUpdatedEvent),
                    Payload = System.Text.Json.JsonSerializer.Serialize(evt),
                    CreatedAtUtc = DateTime.UtcNow,
                    RetryCount = 0
                });

                await _db.SaveChangesAsync();
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
