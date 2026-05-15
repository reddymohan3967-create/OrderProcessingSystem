namespace Shared.Contracts.DTOs;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Request DTO used to create a new order.
/// Contains the customer email and a collection of items to be ordered.
/// This DTO is validated by data annotations to ensure required fields are present.
/// </summary>
public class CreateOrderRequest
{
    /// <summary>
    /// The customer's email address where order notifications will be sent.
    /// This field is required and must be a valid email address.
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The list of items included in the order. At least one item is required.
    /// Each entry contains product-specific information (see <see cref="OrderItemDto"/>).
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one order item is required.")]
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
}
