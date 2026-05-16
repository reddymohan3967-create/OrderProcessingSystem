using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MassTransit;
using OrderService.Data;
using OrderService.Entities;
using Shared.Contracts.Events;
using Shared.Contracts.Enums;

namespace OrderProcessor;

/// <summary>
/// In-memory batcher that collects order IDs and processes them in periodic batches.
/// Ensures work is durable by seeding from persisted PendingWork and publishing status updates.
/// </summary>
public class OrderProcessingBatcher : IAsyncDisposable
{
    private readonly ConcurrentQueue<Guid> _queue = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<OrderProcessingBatcher> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;
    private readonly bool _processOnEnqueue;
    private readonly SemaphoreSlim _signal = new(0);

    /// <summary>
    /// Creates a new <see cref="OrderProcessingBatcher"/>.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for creating scoped DB contexts.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="config">Configuration to read batcher settings from.</param>
    /// <param name="publishEndpoint">MassTransit publish endpoint used to publish status updates.</param>
    public OrderProcessingBatcher(IServiceScopeFactory scopeFactory, ILogger<OrderProcessingBatcher> logger, IConfiguration config, IPublishEndpoint publishEndpoint)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _publishEndpoint = publishEndpoint;

        // Read configuration from the "Batcher" section so values come from
        // appsettings.json or environment variables (Batcher__IntervalMinutes etc.).
        var minutes = 5;
        var batchSize = 50;
        var processOnEnqueue = true;
        try
        {
            var section = config?.GetSection("Batcher");
            if (section != null)
            {
                minutes = section.GetValue<int>("IntervalMinutes", minutes);
                batchSize = section.GetValue<int>("BatchSize", batchSize);
                processOnEnqueue = section.GetValue<bool>("ProcessOnEnqueue", processOnEnqueue);
            }
            else
            {
                minutes = config?.GetValue<int?>("Batcher:IntervalMinutes") ?? minutes;
                batchSize = config?.GetValue<int?>("Batcher:BatchSize") ?? batchSize;
                processOnEnqueue = config?.GetValue<bool?>("Batcher:ProcessOnEnqueue") ?? processOnEnqueue;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read batcher configuration, using defaults");
        }

        _interval = TimeSpan.FromMinutes(Math.Max(1, minutes));
        _batchSize = Math.Max(1, batchSize);
        _processOnEnqueue = processOnEnqueue;

        _logger.LogInformation("OrderProcessingBatcher starting with interval {Minutes}m and batch size {BatchSize} (ProcessOnEnqueue={ProcessOnEnqueue})", minutes, _batchSize, _processOnEnqueue);

        _worker = Task.Run(() => RunAsync(_cts.Token));

        // Seed any existing PendingWork or Pending orders so old work is processed after restart
        // Run in background so constructor stays non-blocking
        Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var pendingWorkIds = await db.PendingWork
                    .OrderBy(p => p.EnqueuedAtUtc)
                    .Select(p => p.OrderId)
                    .ToListAsync();

                var pendingOrderIds = await db.Orders
                    .Where(o => o.Status == OrderStatus.Pending)
                    .Select(o => o.Id)
                    .ToListAsync();

                var allIds = pendingWorkIds.Concat(pendingOrderIds).Distinct().ToList();

                foreach (var id in allIds)
                {
                    Enqueue(id);
                }
                if (allIds.Count > 0)
                {
                    _logger.LogInformation("Seeded {Count} pending orders into batcher on startup", allIds.Count);
                    // If configured to not process immediately on enqueue, kick the worker once
                    // so seeded work is handled at least once on startup as requested.
                    if (!_processOnEnqueue)
                    {
                        try { _signal.Release(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed pending orders into batcher");
            }
        });
    }

    /// <summary>
    /// Enqueue an order id for batch processing. This method is safe to call from multiple threads.
    /// </summary>
    /// <param name="orderId">Order identifier to enqueue.</param>
    public void Enqueue(Guid orderId)
    {
        _queue.Enqueue(orderId);
        // signal worker to wake and process sooner than the full interval only if configured
        if (_processOnEnqueue)
        {
            try { _signal.Release(); } catch { }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    private async Task RunAsync(CancellationToken token)
    {
        var buffer = new List<Guid>();

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Wait until signaled or interval elapses
                await _signal.WaitAsync(_interval, token);

                // Drain up to batch size; keep processing batches while queue has items so we don't wait again
                do
                {
                    while (buffer.Count < _batchSize && _queue.TryDequeue(out var id))
                    {
                        buffer.Add(id);
                    }

                    if (buffer.Count == 0)
                        break;

                    // If in-memory queue was empty, try to fetch pending work from DB as durable source
                    if (buffer.Count == 0)
                    {
                        using var scopeFetch = _scopeFactory.CreateScope();
                        var dbFetch = scopeFetch.ServiceProvider.GetRequiredService<AppDbContext>();
                        var ids = await dbFetch.PendingWork
                            .OrderBy(p => p.EnqueuedAtUtc)
                            .Select(p => p.OrderId)
                            .Take(_batchSize)
                            .ToListAsync(token);

                        foreach (var id in ids)
                        {
                            buffer.Add(id);
                        }
                    }

                    if (buffer.Count == 0)
                    {
                        // nothing to do
                        continue;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var distinctIds = buffer.Distinct().ToList();

                    // Load orders and update status
                    var orders = await db.Orders.Where(o => distinctIds.Contains(o.Id)).ToListAsync(token);

                    // OutboxMessage payloads for OrderCreatedEvent that have PublishedAtUtc set.
                    var publishedOrderIds = new HashSet<Guid>();
                    try
                    {
                        // Include outbox rows for the OrderCreatedEvent regardless of whether the
                        // DB PublishedAtUtc column is set. Some older rows may not have the column
                        // populated but the payload contains PublishedAtUtc. Treat a message as
                        // published if either the DB PublishedAtUtc is set or the payload's
                        // PublishedAtUtc property is present.
                        var publishedRows = await db.OutboxMessages
                            .Where(m => m.EventType == nameof(OrderCreatedEvent))
                            .Select(m => new { m.Payload, m.PublishedAtUtc })
                            .ToListAsync(token);

                        foreach (var row in publishedRows)
                        {
                            try
                            {
                                var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(row.Payload);
                                if (evt != null)
                                {
                                    // consider published if DB column exists or payload contains it
                                    if (row.PublishedAtUtc != null || evt.PublishedAtUtc.HasValue)
                                        publishedOrderIds.Add(evt.OrderId);
                                }
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load published outbox messages for validation");
                    }

                    var updated = 0;
                    var toNotify = new List<(Guid OrderId, OrderStatus OldStatus)>();
                    foreach (var order in orders)
                    {
                        // Only advance orders that are Pending and for which we have evidence
                        // the OrderCreated event was published by the outbox.
                        if (order.Status == OrderStatus.Pending && publishedOrderIds.Contains(order.Id))
                        {
                            var old = order.Status;
                            order.Status = OrderStatus.Processing;
                            order.StatusUpdatedAtUtc = DateTime.UtcNow;
                            updated++;
                            toNotify.Add((order.Id, old));
                        }
                        else if (order.Status == OrderStatus.Pending)
                        {
                            _logger.LogInformation("Skipping order {OrderId} in batch because its OrderCreated event was not published", order.Id);
                        }
                    }

                    // Remove persisted PendingWork entries for these orders (durable ACK)
                    var pendingWorks = await db.PendingWork.Where(p => distinctIds.Contains(p.OrderId)).ToListAsync(token);
                    if (pendingWorks.Count > 0)
                    {
                        db.PendingWork.RemoveRange(pendingWorks);
                    }

                    if (updated > 0 || pendingWorks.Count > 0)
                    {
                        // Create outbox messages for updated orders so the existing outbox
                        // publisher will send them to RabbitMQ. Doing this before SaveChanges
                        // makes the status update and outbox insert atomic.
                        if (updated > 0)
                        {
                            var outboxMessages = new List<OutboxMessage>();
                            foreach (var n in toNotify)
                            {
                                var ord = orders.FirstOrDefault(o => o.Id == n.OrderId);
                                var evt = new OrderStatusUpdatedEvent
                                {
                                    OrderId = n.OrderId,
                                    OldStatus = n.OldStatus,
                                    NewStatus = OrderStatus.Processing,
                                    UpdatedAtUtc = ord?.StatusUpdatedAtUtc ?? DateTime.UtcNow,
                                    Email = ord?.Email ?? string.Empty
                                };

                                outboxMessages.Add(new OutboxMessage
                                {
                                    Id = Guid.NewGuid(),
                                    EventType = nameof(OrderStatusUpdatedEvent),
                                    Payload = JsonSerializer.Serialize(evt),
                                    CreatedAtUtc = DateTime.UtcNow,
                                    RetryCount = 0
                                });
                            }

                            if (outboxMessages.Count > 0)
                                db.OutboxMessages.AddRange(outboxMessages);
                        }

                        await db.SaveChangesAsync(token);

                        if (updated > 0)
                            _logger.LogInformation("Batcher updated {Count} orders to Processing", updated);
                        if (pendingWorks.Count > 0)
                            _logger.LogInformation("Batcher removed {Count} PendingWork rows", pendingWorks.Count);
                        if (updated > 0)
                            _logger.LogInformation("Batcher created {Count} Outbox messages for status updates", toNotify.Count);
                    }

                    buffer.Clear();
                }
                while (!_queue.IsEmpty);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order batch");
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
    }

    /// <summary>
    /// Dispose the batcher, stopping background workers and releasing resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _worker;
        }
        catch { }
        _cts.Dispose();
        _signal.Dispose();
    }
}
