using Microsoft.AspNetCore.Mvc;
using OrderService.Interfaces;
using Shared.Contracts.DTOs;
using Shared.Contracts.Enums;

namespace OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> CreateOrder(CreateOrderRequest request)
    {
        var result = await orderService.CreateOrderAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> GetById(Guid id)
    {
        var order = await orderService.GetOrderByIdAsync(id);

        if (order is null)
            return NotFound();

        return Ok(order);
    }

    [HttpGet]
    public async Task<ActionResult<List<OrderResponse>>> GetAll([FromQuery] OrderStatus? status)
    {
        var orders = await orderService.GetOrdersAsync(status);
        return Ok(orders);
    }

    [HttpGet("cancelled")]
    public async Task<IActionResult> GetCancelled()
    {
        var orders = await orderService.GetOrdersAsync(OrderStatus.Cancelled);
        return Ok(orders);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        try
        {
            var cancelled = await orderService.CancelOrderAsync(id);

            if (!cancelled)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
