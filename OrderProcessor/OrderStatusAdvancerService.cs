using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using Shared.Contracts.Enums;
using Shared.Contracts.Events;
using System.Data;

namespace OrderProcessor;

public class OrderStatusAdvancerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderStatusAdvancerService> _logger;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _pendingCheckInterval;
    private readonly TimeSpan _toProcessingDelay;
    private readonly string _statusSource;

    public OrderStatusAdvancerService(IServiceScopeFactory scopeFactory, ILogger<OrderStatusAdvancerService> logger, IConfiguration config, IPublishEndpoint publishEndpoint)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _publishEndpoint = publishEndpoint;

        var checkSeconds = 30;
        var pendingCheckMinutes = 5;
        var toProcessingMinutes = 5;

        try
        {
            var s = config?["OrderAdvancer:CheckSeconds"];
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, out var cs)) checkSeconds = cs;

            var m1 = config?["OrderAdvancer:ToProcessingMinutes"];
            if (!string.IsNullOrEmpty(m1) && int.TryParse(m1, out var mm1)) toProcessingMinutes = mm1;

            var pcm = config?["OrderAdvancer:PendingCheckMinutes"];
            if (!string.IsNullOrEmpty(pcm) && int.TryParse(pcm, out var pcmv)) pendingCheckMinutes = pcmv;
        }
        catch { }

        _checkInterval = TimeSpan.FromSeconds(Math.Max(1, checkSeconds));
        _pendingCheckInterval = TimeSpan.FromMinutes(Math.Max(1, pendingCheckMinutes));
        _toProcessingDelay = TimeSpan.FromMinutes(Math.Max(0, toProcessingMinutes));

        _statusSource = config?["OrderAdvancer:StatusSource"] ?? "Orders";

        _logger.LogInformation("OrderStatusAdvancer configured: check every {Seconds}s, toProcessing {ToProcessing}m, statusSource {Source}", _checkInterval.TotalSeconds, _toProcessingDelay.TotalMinutes, _statusSource);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderStatusAdvancerService started");

        var lastPendingCheck = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var now = DateTime.UtcNow;

                // Advance Pending -> Processing on configured interval
                if ((now - lastPendingCheck) >= _pendingCheckInterval)
                {
                    lastPendingCheck = now;
                    var toProcessingCutoff = now - _toProcessingDelay;
                    var pendingIds = await GetOrderIdsByStatusFromSource(db, _statusSource, OrderStatus.Pending, toProcessingCutoff, stoppingToken);
                    var pending = pendingIds.Count == 0 ? new List<OrderService.Entities.Order>() : await db.Orders.Where(o => pendingIds.Contains(o.Id)).ToListAsync(stoppingToken);

                    foreach (var o in pending)
                    {
                        var old = o.Status;
                        o.Status = OrderStatus.Processing;
                        o.StatusUpdatedAtUtc = DateTime.UtcNow;
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

                    if (pending.Count > 0)
                    {
                        await db.SaveChangesAsync(stoppingToken);
                    }
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

    private static async Task<List<Guid>> GetOrderIdsByStatusFromSource(AppDbContext db, string sourceName, OrderStatus status, DateTime cutoff, CancellationToken ct)
    {
        // If configured to use the Orders table, query via EF directly.
        if (string.Equals(sourceName, "Orders", StringComparison.OrdinalIgnoreCase))
        {
            return await db.Orders.Where(o => o.Status == status && o.StatusUpdatedAtUtc <= cutoff).Select(o => o.Id).ToListAsync(ct);
        }

        // Otherwise attempt to read from the configured source (likely a view) using raw SQL.
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT Id FROM \"{sourceName}\" WHERE Status = @p0 AND StatusUpdatedAtUtc <= @p1";

            var p0 = cmd.CreateParameter();
            p0.ParameterName = "@p0";
            p0.Value = (int)status;
            cmd.Parameters.Add(p0);

            var p1 = cmd.CreateParameter();
            p1.ParameterName = "@p1";
            p1.Value = cutoff;
            cmd.Parameters.Add(p1);

            var ids = new List<Guid>();
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                {
                    var val = reader.GetValue(0);
                    if (val is Guid g) ids.Add(g);
                    else
                    {
                        var s = val?.ToString();
                        if (!string.IsNullOrEmpty(s) && Guid.TryParse(s, out var gg)) ids.Add(gg);
                    }
                }
            }

            return ids;
        }
        catch
        {
            // Fallback to EF query if raw SQL fails.
            return await db.Orders.Where(o => o.Status == status && o.StatusUpdatedAtUtc <= cutoff).Select(o => o.Id).ToListAsync(ct);
        }
    }
}
