using Shared.Contracts.Enums;

namespace Shared.Contracts.DTOs;

public class OrderResponse
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
}
