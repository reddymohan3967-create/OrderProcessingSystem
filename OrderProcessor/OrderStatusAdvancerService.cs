using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OrderService.Data;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Enums;
using Shared.Contracts.Events;
using MassTransit;

namespace OrderProcessor;

public class OrderStatusAdvancerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderStatusAdvancerService> _logger;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _toShippedDelay;
    private readonly TimeSpan _toDeliveredDelay;

    public OrderStatusAdvancerService(IServiceScopeFactory scopeFactory, ILogger<OrderStatusAdvancerService> logger, IConfiguration config, IPublishEndpoint publishEndpoint)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _publishEndpoint = publishEndpoint;

        var checkSeconds = 30;
        var toShippedMinutes = 5;
        var toDeliveredMinutes = 10; // default total minutes from Processing -> Delivered
        try
        {
            var s = config?["OrderAdvancer:CheckSeconds"];
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, out var cs)) checkSeconds = cs;

            var m1 = config?["OrderAdvancer:ToShippedMinutes"];
            if (!string.IsNullOrEmpty(m1) && int.TryParse(m1, out var mm1)) toShippedMinutes = mm1;

            var m2 = config?["OrderAdvancer:ToDeliveredMinutes"];
            if (!string.IsNullOrEmpty(m2) && int.TryParse(m2, out var mm2)) toDeliveredMinutes = mm2;
        }
        catch { }

        _checkInterval = TimeSpan.FromSeconds(Math.Max(1, checkSeconds));
        _toShippedDelay = TimeSpan.FromMinutes(Math.Max(0, toShippedMinutes));
        _toDeliveredDelay = TimeSpan.FromMinutes(Math.Max(0, toDeliveredMinutes));

        _logger.LogInformation("OrderStatusAdvancer configured: check every {Seconds}s, toShipped {ToShipped}m, toDelivered {ToDelivered}m", _checkInterval.TotalSeconds, _toShippedDelay.TotalMinutes, _toDeliveredDelay.TotalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderStatusAdvancerService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var now = DateTime.UtcNow;

                // Advance Processing -> Shipped when Processing was set earlier than cutoff
                var toShipCutoff = now - _toShippedDelay;
                var processing = await db.Orders.Where(o => o.Status == OrderStatus.Processing && o.StatusUpdatedAtUtc <= toShipCutoff).ToListAsync(stoppingToken);
                foreach (var o in processing)
                {
                    var old = o.Status;
                    o.Status = OrderStatus.Shipped;
                    o.StatusUpdatedAtUtc = now;
                    _logger.LogInformation("Order {OrderId} advanced {Old}->{New}", o.Id, old, o.Status);
                    try
                    {
                        await _publishEndpoint.Publish(new OrderStatusUpdatedEvent
                        {
                            OrderId = o.Id,
                            OldStatus = old,
                            NewStatus = o.Status,
                            UpdatedAtUtc = o.StatusUpdatedAtUtc,
                            Email = o.Email ?? string.Empty
                        }, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to publish status update for Order {OrderId}", o.Id);
                    }
                }

                // Advance Shipped -> Delivered when shipped timestamp older than delivered cutoff
                var toDeliveredCutoff = now - _toDeliveredDelay;
                var toDeliver = await db.Orders.Where(o => o.Status == OrderStatus.Shipped && o.StatusUpdatedAtUtc <= toDeliveredCutoff).ToListAsync(stoppingToken);
                foreach (var o in toDeliver)
                {
                    var old = o.Status;
                    o.Status = OrderStatus.Delivered;
                    o.StatusUpdatedAtUtc = now;
                    _logger.LogInformation("Order {OrderId} advanced {Old}->{New}", o.Id, old, o.Status);
                    try
                    {
                        await _publishEndpoint.Publish(new OrderStatusUpdatedEvent
                        {
                            OrderId = o.Id,
                            OldStatus = old,
                            NewStatus = o.Status,
                            UpdatedAtUtc = o.StatusUpdatedAtUtc,
                            Email = o.Email ?? string.Empty
                        }, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to publish status update for Order {OrderId}", o.Id);
                    }
                }

                if (processing.Count > 0 || toDeliver.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error advancing order statuses");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("OrderStatusAdvancerService stopped");
    }
}
