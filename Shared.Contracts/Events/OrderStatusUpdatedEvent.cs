using Shared.Contracts.Enums;

namespace Shared.Contracts.Events;

public class OrderStatusUpdatedEvent
{
    public Guid OrderId { get; set; }
    public OrderStatus OldStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string Email { get; set; } = string.Empty;
}
