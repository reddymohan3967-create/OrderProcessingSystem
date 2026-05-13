using Shared.Contracts.DTOs;
using Shared.Contracts.Enums;

namespace Shared.Contracts.Events;

public class OrderCreatedEvent
{
    public Guid OrderId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
}
