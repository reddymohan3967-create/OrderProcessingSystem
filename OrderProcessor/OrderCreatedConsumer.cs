using MassTransit;
using Shared.Contracts.Events;
using OrderService.Data;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Enums;
using OrderService.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace OrderProcessor;

public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly OrderProcessingBatcher _batcher;
    private readonly ILogger<OrderCreatedConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public OrderCreatedConsumer(OrderProcessingBatcher batcher, ILogger<OrderCreatedConsumer> logger, IServiceScopeFactory scopeFactory)
    {
        _batcher = batcher;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

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

        // Try to insert a processed-message record. If another consumer already inserted it
        // concurrently, the DB will raise a duplicate-key error and we treat that as already processed.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Mark message as processed and update order status atomically so the ACK and
        // status transition are durable even if the in-memory batcher is down.
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == context.Message.OrderId);
        if (order != null && order.Status == OrderStatus.Pending)
        {
            order.Status = OrderStatus.Processing;
            order.StatusUpdatedAtUtc = DateTime.UtcNow;
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
