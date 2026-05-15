namespace OrderService.Entities;

/// <summary>
/// Represents a product available for ordering. This entity is used for
/// lookups and display; pricing and icons are stored as strings for simplicity.
/// </summary>
public class Product
{
    /// <summary>
    /// Primary key for the product.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Display name of the product.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Price represented as string (simple placeholder; consider decimal for real systems).
    /// </summary>
    public string Price { get; set; } = string.Empty;

    /// <summary>
    /// Optional icon identifier or URL for the product.
    /// </summary>
    public string Icon { get; set; } = string.Empty;
}
