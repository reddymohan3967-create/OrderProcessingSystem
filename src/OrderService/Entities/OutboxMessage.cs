namespace OrderService.Entities;

/// <summary>
/// Represents an outbox message stored in the local database until it is
/// published to the message broker. Used to ensure reliable, transactional
/// publishes from the originating service.
/// </summary>
public class OutboxMessage
{
    /// <summary>
    /// Unique identifier for the outbox entry. Also used as MessageId when publishing.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The CLR event type name stored so the outbox worker can deserialize the payload.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// JSON payload of the event to publish.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// When the outbox record was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When the message was published to the broker. Null until published.
    /// </summary>
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>
    /// Number of times the worker retried publishing this message after failures.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Optional error message from the last publish attempt.
    /// </summary>
    public string? Error { get; set; }
}
