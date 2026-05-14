namespace Shared.Contracts.DTOs;

using System.ComponentModel.DataAnnotations;

public class CreateOrderRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(1, ErrorMessage = "At least one order item is required.")]
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
}
