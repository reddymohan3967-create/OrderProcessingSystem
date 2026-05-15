using Shared.Contracts.Enums;

namespace Shared.Contracts.Events;

/// <summary>
/// Event published when an order's status changes. Consumers may use this to
/// trigger notifications or downstream state transitions.
/// </summary>
public class OrderStatusUpdatedEvent
{
    /// <summary>
    /// The id of the order whose status changed.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Previous status before the update.
    /// </summary>
    public OrderStatus OldStatus { get; set; }

    /// <summary>
    /// New status after the update.
    /// </summary>
    public OrderStatus NewStatus { get; set; }

    /// <summary>
    /// UTC timestamp when the status update occurred.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Customer email associated with the order (useful for notification services).
    /// </summary>
    public string Email { get; set; } = string.Empty;
}
