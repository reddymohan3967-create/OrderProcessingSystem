using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OrderService.Data;
using OrderService.Entities;
using Shared.Contracts.Events;
using Shared.Contracts.Enums;

namespace OrderService.Controllers;

[ApiController]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("PerIpPolicy")]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    public class SendOrderProcessedRequest
    {
        [Required]
        public Guid OrderId { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    // POST api/notifications/order-processed
    // Updates order status to Processing (if present) and enqueues an OrderStatusUpdatedEvent
    [HttpPost("order-processed")]
    public async Task<IActionResult> SendOrderProcessed([FromBody] SendOrderProcessedRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        await using var tx = await _db.Database.BeginTransactionAsync();

        OrderStatus oldStatus = OrderStatus.Pending;
        var order = await _db.Orders.FindAsync(request.OrderId);
        if (order != null)
        {
            oldStatus = order.Status;
            order.Status = OrderStatus.Processing;
            // ensure we have the latest recipient email
            order.Email = request.Email;
        }

        var evt = new OrderStatusUpdatedEvent
        {
            OrderId = request.OrderId,
            OldStatus = oldStatus,
            NewStatus = OrderStatus.Processing,
            UpdatedAtUtc = DateTime.UtcNow,
            Email = request.Email
        };

        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = nameof(OrderStatusUpdatedEvent),
            Payload = JsonSerializer.Serialize(evt),
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };

        _db.OutboxMessages.Add(outbox);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Accepted(new { request.OrderId });
    }
}
