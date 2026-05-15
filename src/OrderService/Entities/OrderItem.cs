namespace OrderService.Entities;

/// <summary>
/// Entity representing a single order line item associated with an Order.
/// </summary>
public class OrderItem
{
    /// <summary>
    /// Primary key for the order item.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key referencing the parent Order.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Navigation property back to the Order entity.
    /// </summary>
    public Order Order { get; set; } = null!;

    /// <summary>
    /// Human-friendly product name.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Quantity ordered for this product.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Unit price for the product.
    /// </summary>
    public decimal UnitPrice { get; set; }
}
