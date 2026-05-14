using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrderService.Data;
using MassTransit;
using Shared.Contracts.Events;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Enums;

namespace OrderProcessor;

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
    private readonly SemaphoreSlim _signal = new(0);

    public OrderProcessingBatcher(IServiceScopeFactory scopeFactory, ILogger<OrderProcessingBatcher> logger, IConfiguration config, IPublishEndpoint publishEndpoint)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _publishEndpoint = publishEndpoint;

        var minutes = 5;
        var batchSize = 50;
        try
        {
            var s = config?["Batcher:IntervalMinutes"];
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, out var m)) minutes = m;

            var bs = config?["Batcher:BatchSize"];
            if (!string.IsNullOrEmpty(bs) && int.TryParse(bs, out var b)) batchSize = b;
        }
        catch { }

        _interval = TimeSpan.FromMinutes(minutes);
        _batchSize = Math.Max(1, batchSize);

        _logger.LogInformation("OrderProcessingBatcher starting with interval {Minutes}m and batch size {BatchSize}", minutes, _batchSize);

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
                    _logger.LogInformation("Seeded {Count} pending orders into batcher on startup", allIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed pending orders into batcher");
            }
        });
    }

    public void Enqueue(Guid orderId)
    {
        _queue.Enqueue(orderId);
        // signal worker to wake and process sooner than the full interval
        try { _signal.Release(); } catch { }
    }

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

                var updated = 0;
                var toNotify = new List<(Guid OrderId, OrderStatus OldStatus)>();
                foreach (var order in orders)
                {
                    if (order.Status == OrderStatus.Pending)
                    {
                        var old = order.Status;
                        order.Status = OrderStatus.Processing;
                        order.StatusUpdatedAtUtc = DateTime.UtcNow;
                        updated++;
                        toNotify.Add((order.Id, old));
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
                    await db.SaveChangesAsync(token);

                    if (updated > 0)
                        _logger.LogInformation("Batcher updated {Count} orders to Processing", updated);
                    if (pendingWorks.Count > 0)
                        _logger.LogInformation("Batcher removed {Count} PendingWork rows", pendingWorks.Count);

                    // Publish OrderStatusUpdatedEvent for each updated order so downstream
                    // services (notifications, audits) can react. Fire-and-forget publish.
                foreach (var n in toNotify)
                    {
                        try
                        {
                            var ord = orders.FirstOrDefault(o => o.Id == n.OrderId);
                            await _publishEndpoint.Publish(new OrderStatusUpdatedEvent
                            {
                                OrderId = n.OrderId,
                                OldStatus = n.OldStatus,
                                NewStatus = OrderStatus.Processing,
                                UpdatedAtUtc = ord?.StatusUpdatedAtUtc ?? DateTime.UtcNow,
                                Email = ord?.Email ?? string.Empty
                            }, token);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to publish OrderStatusUpdatedEvent for OrderId {OrderId}", n.OrderId);
                        }
                    }
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
