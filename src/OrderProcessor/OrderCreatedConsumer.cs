using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Entities;
using Shared.Contracts.Enums;
using Shared.Contracts.Events;
using System.Text.Json;

namespace OrderProcessor;

/// <summary>
/// OrderCreatedConsumer is a MassTransit consumer that listens for OrderCreatedEvent messages. When it receives an event, it performs idempotent processing to ensure that each message is only processed once, even in the face of duplicates. It checks for a MessageId to track processed messages and uses the PublishedAtUtc timestamp to ensure that only messages published by the outbox publisher are processed. The consumer updates the order status to Processing and enqueues the order ID for batch processing by the OrderProcessingBatcher. It also handles potential database concurrency issues when marking messages as processed and ensures durability of the enqueue operation by persisting a PendingWork record in the database. This design allows for reliable and idempotent processing of order creation events while maintaining visibility into processing through logging.
/// </summary>
public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    /// <summary>
    /// batcher is an instance of OrderProcessingBatcher, which is responsible for batching order processing work. When an OrderCreatedEvent is consumed, the consumer enqueues the order ID into the batcher for later processing. This allows for efficient handling of multiple orders by processing them in batches rather than individually, which can improve performance and reduce resource contention when there are high volumes of orders being created.
    /// </summary>
    private readonly OrderProcessingBatcher _batcher;

    /// <summary>
    /// OrderCreatedConsumer uses an ILogger<OrderCreatedConsumer> to log information about the processing of OrderCreatedEvent messages. This includes logging when messages are enqueued for batch processing, as well as any warnings or errors that occur during processing. The logger provides visibility into the consumer's activity and helps with troubleshooting and monitoring the flow of events through the system.
    /// </summary>
    private readonly ILogger<OrderCreatedConsumer> _logger;

    /// <summary>
    /// IServiceScopeFactory is used to create a new scope for each consumed message, allowing the consumer to resolve scoped services such as the AppDbContext. This is important for ensuring that database operations are performed within the appropriate scope and that resources are properly managed. By creating a new scope for each message, we can ensure that the consumer can safely interact with the database and other scoped services without risking conflicts or resource leaks across multiple messages being processed concurrently.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Creates a new <see cref="OrderCreatedConsumer"/> instance.
    /// </summary>
    /// <param name="batcher">Batcher used to enqueue work for later processing.</param>
    /// <param name="logger">Logger used to record processing events and errors.</param>
    /// <param name="scopeFactory">Service scope factory used to create a scoped <see cref="OrderService.Data.AppDbContext"/> for DB operations.</param>
    public OrderCreatedConsumer(OrderProcessingBatcher batcher, ILogger<OrderCreatedConsumer> logger, IServiceScopeFactory scopeFactory)
    {
        _batcher = batcher;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Consumes an <see cref="Shared.Contracts.Events.OrderCreatedEvent"/>, performs idempotent handling
    /// and enqueues the order for batched processing. The method records a processed-message marker
    /// in the database to prevent duplicate handling and persists a durable <c>PendingWork</c> row so the
    /// enqueue is durable across restarts.
    /// </summary>
    /// <param name="context">MassTransit consume context containing the <see cref="OrderCreatedEvent"/> message and headers.</param>
    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        // Idempotency: ensure this message is only processed once using the MessageId set by publisher
        var messageId = context.MessageId ?? Guid.Empty;
        if (messageId == Guid.Empty)
        {
            // fallback to order id-based enqueue if no message id present
            _batcher.Enqueue(context.Message.OrderId);
            _logger.LogDebug("Enqueued OrderId {OrderId} for batch processing (no MessageId)", context.Message.OrderId);
            return;
        }

        // Ensure this message was published by the outbox publisher. Prefer the PublishedAtUtc header but
        // fall back to the event payload's PublishedAtUtc for compatibility and reliability.
        DateTime? publishedAt = null;
        try
        {
            if (context.Headers.TryGetHeader("PublishedAtUtc", out var publishedHeader) && publishedHeader != null)
            {
            if (publishedHeader is DateTime dt)
            {
                publishedAt = dt;
            }
            else
            {
                if (DateTime.TryParse(publishedHeader.ToString(), out var parsed))
                    publishedAt = parsed;
                else
                    publishedAt = null;
            }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read PublishedAtUtc header for OrderId {OrderId}", context.Message.OrderId);
        }

        if (publishedAt == null && context.Message.PublishedAtUtc.HasValue)
            publishedAt = context.Message.PublishedAtUtc.Value;

        if (publishedAt == null)
        {
            _logger.LogInformation("Skipping processing of OrderId {OrderId} because PublishedAtUtc was not provided", context.Message.OrderId);
            return;
        }

        // Try to insert a processed-message record. If another consumer already inserted it
        // concurrently, the DB will raise a duplicate-key error and we treat that as already processed.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Mark message as processed and update order status atomically so the ACK and
        // status transition are durable even if the in-memory batcher is down.
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == context.Message.OrderId);
        OrderStatus oldStatus = OrderStatus.Pending;
        if (order != null)
        {
            oldStatus = order.Status;
            if (order.Status == OrderStatus.Pending)
            {
                order.Status = OrderStatus.Processing;
                order.StatusUpdatedAtUtc = DateTime.UtcNow;

                // Create an outbox message for the status update so notification
                // consumers receive the Processing transition.
                try
                {
                    var evtStatus = new Shared.Contracts.Events.OrderStatusUpdatedEvent
                    {
                        OrderId = order.Id,
                        OldStatus = oldStatus,
                        NewStatus = OrderStatus.Processing,
                        UpdatedAtUtc = order.StatusUpdatedAtUtc,
                        Email = order.Email ?? string.Empty
                    };

                    db.OutboxMessages.Add(new OrderService.Entities.OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = nameof(Shared.Contracts.Events.OrderStatusUpdatedEvent),
                        Payload = JsonSerializer.Serialize(evtStatus),
                        CreatedAtUtc = DateTime.UtcNow,
                        RetryCount = 0
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create outbox message for OrderId {OrderId}", order.Id);
                }
            }
        }

        db.ProcessedMessages.Add(new ProcessedMessage { Id = messageId, ProcessedAtUtc = DateTime.UtcNow });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Different DB providers report unique constraint violations differently.
            // For SQLite the SqliteException.SqliteErrorCode == 19 indicates constraint violation.
            // For Postgres (Npgsql) the PostgresException.SqlState == "23505" indicates unique_violation.
            var isUniqueViolation = false;

            try
            {
                var sqliteEx = ex.InnerException as Microsoft.Data.Sqlite.SqliteException;
                if (sqliteEx != null && sqliteEx.SqliteErrorCode == 19)
                    isUniqueViolation = true;
            }
            catch { }

            try
            {
                var inner = ex.InnerException;
                if (inner != null && inner.GetType().FullName == "Npgsql.PostgresException")
                {
                    var prop = inner.GetType().GetProperty("SqlState");
                    if (prop != null)
                    {
                        var val = prop.GetValue(inner) as string;
                        if (val == "23505") isUniqueViolation = true;
                    }
                }
            }
            catch { }

            if (isUniqueViolation)
            {
                _logger.LogInformation("Processed message {MessageId} already exists, skipping", messageId);
                return;
            }

            // Unknown DB error - rethrow so it can be observed and retried
            throw;
        }

        // Successfully recorded this message as processed; proceed
        // Also persist a PendingWork row so enqueue is durable across restarts.
        try
        {
            var existing = await db.Set<OrderService.Entities.PendingWork>()
                .FirstOrDefaultAsync(p => p.OrderId == context.Message.OrderId);

            if (existing == null)
            {
                db.Set<OrderService.Entities.PendingWork>().Add(new OrderService.Entities.PendingWork
                {
                    Id = Guid.NewGuid(),
                    OrderId = context.Message.OrderId,
                    EnqueuedAtUtc = DateTime.UtcNow
                });

                try
                {
                    await db.SaveChangesAsync();
                }
                catch
                {
                    // ignore unique-constraint races and continue to enqueue in-memory
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist PendingWork for OrderId {OrderId}", context.Message.OrderId);
        }

        _batcher.Enqueue(context.Message.OrderId);
        _logger.LogDebug("Enqueued OrderId {OrderId} for batch processing", context.Message.OrderId);
    }
}
