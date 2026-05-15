using Shared.Contracts.Enums;

namespace Shared.Contracts.DTOs;

/// <summary>
/// Response DTO returned after creating or querying an order.
/// Contains order metadata and the list of ordered items.
/// </summary>
public class OrderResponse
{
    /// <summary>
    /// Unique identifier of the order.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// UTC timestamp when the order was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Current status of the order (Pending, Processing, Completed, etc.).
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// Total monetary amount for the order.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Customer email associated with the order.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The list of items included in the order.
    /// </summary>
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
}
