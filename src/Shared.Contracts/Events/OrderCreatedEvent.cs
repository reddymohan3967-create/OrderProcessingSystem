using Shared.Contracts.DTOs;
using Shared.Contracts.Enums;

namespace Shared.Contracts.Events;

/// <summary>
/// Event published when a new order is created.
/// This event is intended for cross-service communication and contains the
/// order details necessary for downstream processing (e.g. notification, fulfillment).
/// </summary>
public class OrderCreatedEvent
{
    /// <summary>
    /// Identifier of the created order.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// UTC timestamp when the order was created in the originating service.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// The time the event was published by the outbox publisher. This is set by the
    /// producer and can be used by consumers to validate that the message was published
    /// by the outbox (payload-level fallback for header-based checks).
    /// </summary>
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>
    /// The initial status of the order at creation time.
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// Total monetary amount for the order.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Collection of items included in the order.
    /// </summary>
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
}
