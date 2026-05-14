using Microsoft.AspNetCore.Mvc;
using OrderService.Interfaces;
using System.Linq;
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

    public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
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
            var force = User.IsInRole("Admin");
            var ok = await _orderService.UpdateOrderStatusAsync(id, OrderStatus.Shipped, force);
            if (!ok) return NotFound();
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
            var force = User.IsInRole("Admin");
            var ok = await _orderService.UpdateOrderStatusAsync(id, OrderStatus.Delivered, force);
            if (!ok) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
