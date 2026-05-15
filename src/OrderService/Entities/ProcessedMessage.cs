namespace OrderService.Entities;

/// <summary>
/// Tracks message ids that have been processed by consumers to provide
/// idempotency for event handling across restarts and multiple consumers.
/// </summary>
public class ProcessedMessage
{
    /// <summary>
    /// Message id (usually the OutboxMessage.Id) that was processed.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// UTC timestamp when the message was processed and recorded.
    /// </summary>
    public DateTime ProcessedAtUtc { get; set; }
}
