namespace Shared.Contracts.Enums;

/// <summary>
/// Represents the lifecycle status of an order.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order has been created but not yet processed.</summary>
    Pending = 1,
    /// <summary>Order is being processed by backend systems.</summary>
    Processing = 2,
    /// <summary>Order has shipped and is in transit.</summary>
    Shipped = 3,
    /// <summary>Order has been delivered to the customer.</summary>
    Delivered = 4,
    /// <summary>Order was cancelled either by user or admin.</summary>
    Cancelled = 5
}
