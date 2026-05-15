namespace Shared.Contracts.DTOs;

/// <summary>
/// Data transfer object representing a single item within an order.
/// Used in create requests and order responses to convey item details.
/// </summary>
public class OrderItemDto
{
    /// <summary>
    /// Human-readable product name.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Quantity of this product in the order.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Unit price for this product.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Computed total for this line (Quantity * UnitPrice).
    /// </summary>
    public decimal LineTotal => Quantity * UnitPrice;
}
