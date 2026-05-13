using Shared.Contracts.Enums;

namespace OrderService.Entities;

public class Order
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }

    public List<OrderItem> Items { get; set; } = new List<OrderItem>();
}
