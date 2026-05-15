using Shared.Contracts.Enums;

namespace OrderService.Entities;

/// <summary>
/// Entity representing an order stored in the application's database.
/// Contains metadata and relationships to order line items.
/// </summary>
public class Order
{
    /// <summary>
    /// Primary key for the order.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// UTC timestamp when the order was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Current order status.
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// UTC timestamp when the status was last updated.
    /// </summary>
    public DateTime StatusUpdatedAtUtc { get; set; }

    /// <summary>
    /// Total amount of the order.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Customer email for notifications.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property for order line items.
    /// </summary>
    public List<OrderItem> Items { get; set; } = new List<OrderItem>();
}
