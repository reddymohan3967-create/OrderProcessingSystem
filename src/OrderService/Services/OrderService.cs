using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Entities;
using OrderService.Interfaces;
using Shared.Contracts.DTOs;
using Shared.Contracts.Enums;
using System.Text.Json;
using Shared.Contracts.Events;

namespace OrderService.Services;

/// <summary>
/// Implementation of <see cref="IOrderService"/> providing basic order lifecycle operations
/// such as creating orders, retrieving orders and updating status. This service uses
/// the application's <see cref="AppDbContext"/> to persist orders and outbox messages.
/// </summary>
public class OrderService : IOrderService
{
    private readonly AppDbContext _dbContext;

    /// <summary>
    /// Creates a new <see cref="OrderService"/>.
    /// </summary>
    /// <param name="dbContext">EF Core DB context used to persist orders and outbox messages.</param>
    public OrderService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Creates a new order and inserts a corresponding outbox message so the
    /// order-created event can be published by the outbox publisher.
    /// </summary>
    /// <param name="request">Order creation request DTO containing items and metadata.</param>
    /// <returns>A mapped <see cref="OrderResponse"/> representing the created order.</returns>
    public async Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("At least one order item is required.");

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            Status = OrderStatus.Pending,
            StatusUpdatedAtUtc = DateTime.UtcNow,
            TotalAmount = request.Items.Sum(i => i.Quantity * i.UnitPrice),
            Email = request.Email,
            Items = request.Items.Select(item => new OrderItem
            {
                Id = Guid.NewGuid(),
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            }).ToList()
        };

        _dbContext.Orders.Add(order);

        var orderCreatedEvent = new OrderCreatedEvent
        {
            OrderId = order.Id,
            CreatedAtUtc = order.CreatedAtUtc,
            Status = order.Status,
            TotalAmount = order.TotalAmount,
            Items = request.Items
        };

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = nameof(OrderCreatedEvent),
            Payload = JsonSerializer.Serialize(orderCreatedEvent),
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return MapToResponse(order);
    }

    /// <summary>
    /// Retrieves an order by its id including order items.
    /// </summary>
    /// <param name="id">Order identifier.</param>
    /// <returns>The mapped <see cref="OrderResponse"/> or null if no order exists.</returns>
    public async Task<OrderResponse?> GetOrderByIdAsync(Guid id)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        return order is null ? null : MapToResponse(order);
    }

    /// <summary>
    /// Returns orders optionally filtered by status.
    /// </summary>
    /// <param name="status">Optional status to filter by.</param>
    /// <returns>List of mapped <see cref="OrderResponse"/> entries.</returns>
    public async Task<List<OrderResponse>> GetOrdersAsync(OrderStatus? status = null)
    {
        var query = _dbContext.Orders
            .Include(o => o.Items)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        var orders = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync();

        return orders.Select(MapToResponse).ToList();
    }

    /// <summary>
    /// Cancels an order if it is in Pending state. This method will create
    /// an outbox message for the status update so consumers are informed.
    /// </summary>
    /// <param name="id">Order identifier to cancel.</param>
    /// <returns>True if the order was cancelled; false if it didn't exist.</returns>
    public async Task<bool> CancelOrderAsync(Guid id)
    {
        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return false;

        if (order.Status != OrderStatus.Pending)
            throw new InvalidOperationException(
                "Only orders in Pending status can be cancelled.");

        // Use a transaction so the status change and outbox entry are atomic
        await using var tx = await _dbContext.Database.BeginTransactionAsync();

        var oldStatus = order.Status;
        order.Status = OrderStatus.Cancelled;
        order.StatusUpdatedAtUtc = DateTime.UtcNow;

        var evt = new OrderStatusUpdatedEvent
        {
            OrderId = order.Id,
            OldStatus = oldStatus,
            NewStatus = OrderStatus.Cancelled,
            UpdatedAtUtc = order.StatusUpdatedAtUtc,
            Email = order.Email ?? string.Empty
        };

        var outbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = nameof(OrderStatusUpdatedEvent),
            Payload = JsonSerializer.Serialize(evt),
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };

        _dbContext.OutboxMessages.Add(outbox);

        await _dbContext.SaveChangesAsync();
        await tx.CommitAsync();

        return true;
    }

    /// <summary>
    /// Updates the order status performing basic validation of allowed transitions
    /// unless <paramref name="force"/> is true. An outbox message is created to
    /// publish the status change.
    /// </summary>
    /// <param name="id">Order identifier.</param>
    /// <param name="newStatus">New status to apply.</param>
    /// <param name="force">When true bypasses standard transition validation.</param>
    /// <returns>True when update succeeded; false when order not found.</returns>
    public async Task<bool> UpdateOrderStatusAsync(Guid id, OrderStatus newStatus, bool force = false)
    {
        var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return false;

        if (!force)
        {
            // Simple validation: allow only forward transitions in common flow
            // Pending -> Processing -> Shipped -> Delivered
            var allowed = (order.Status, newStatus) switch
            {
                (OrderStatus.Pending, OrderStatus.Processing) => true,
                (OrderStatus.Processing, OrderStatus.Shipped) => true,
                (OrderStatus.Shipped, OrderStatus.Delivered) => true,
                // allow cancelling from Pending
                (OrderStatus.Pending, OrderStatus.Cancelled) => true,
                _ => false
            };

            if (!allowed)
                throw new InvalidOperationException($"Invalid status transition from {order.Status} to {newStatus}");
        }

        // Use a transaction so the status change and outbox entry are atomic
        await using var tx = await _dbContext.Database.BeginTransactionAsync();

        var oldStatus = order.Status;
        order.Status = newStatus;
        order.StatusUpdatedAtUtc = DateTime.UtcNow;

        var evt = new OrderStatusUpdatedEvent
        {
            OrderId = order.Id,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            UpdatedAtUtc = order.StatusUpdatedAtUtc,
            Email = order.Email ?? string.Empty
        };

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = nameof(OrderStatusUpdatedEvent),
            Payload = JsonSerializer.Serialize(evt),
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        await _dbContext.SaveChangesAsync();
        await tx.CommitAsync();

        return true;
    }

    /// <summary>
    /// Maps the EF Order entity to a DTO <see cref="OrderResponse"/>.
    /// </summary>
    /// <param name="order">Entity to map.</param>
    /// <returns>Mapped response DTO.</returns>
    private static OrderResponse MapToResponse(Order order)
    {
        return new OrderResponse
        {
            Id = order.Id,
            CreatedAtUtc = order.CreatedAtUtc,
            Status = order.Status,
            TotalAmount = order.TotalAmount,
            Email = order.Email,
            Items = order.Items.Select(i => new OrderItemDto
            {
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };
    }
}
