using System;

namespace OrderService.Entities;

/// <summary>
/// Represents a durable record of work that needs to be performed for an order.
/// Stored so in-memory batchers can recover and continue processing after restarts.
/// </summary>
public class PendingWork
{
    /// <summary>
    /// Primary key of the pending work record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Order identifier the pending work is associated with.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// UTC timestamp when the work was enqueued.
    /// </summary>
    public DateTime EnqueuedAtUtc { get; set; }
}
