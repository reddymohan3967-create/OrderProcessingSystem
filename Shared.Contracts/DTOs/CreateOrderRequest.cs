namespace Shared.Contracts.DTOs;

public class CreateOrderRequest
{
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
}
