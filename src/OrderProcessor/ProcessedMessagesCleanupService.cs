using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;

namespace OrderProcessor;

public class ProcessedMessagesCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProcessedMessagesCleanupService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _retention;

    public ProcessedMessagesCleanupService(IServiceScopeFactory scopeFactory, ILogger<ProcessedMessagesCleanupService> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Read configuration (defaults if missing)
        var intervalHours = 24;
        var retentionDays = 30;
        try
        {
            var s = config?["ProcessedMessagesCleanup:IntervalHours"];
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, out var ih)) intervalHours = ih;

            var r = config?["ProcessedMessagesCleanup:RetentionDays"];
            if (!string.IsNullOrEmpty(r) && int.TryParse(r, out var rd)) retentionDays = rd;
        }
        catch { }

        _interval = TimeSpan.FromHours(Math.Max(1, intervalHours));
        _retention = TimeSpan.FromDays(Math.Max(1, retentionDays));
        _logger.LogInformation("ProcessedMessagesCleanupService configured with interval {Hours}h and retention {Days}d", _interval.TotalHours, _retention.TotalDays);
    }
    /// <summary>
    /// Background loop which periodically removes old processed message markers from the database
    /// to prevent the ProcessedMessages table from growing indefinitely. The cleanup respects the
    /// configured retention period and runs at the configured interval.
    /// </summary>
    /// <param name="stoppingToken">Token that signals cancellation when the host is shutting down.</param>
    /// <returns>A task representing the lifetime of the cleanup service.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProcessedMessagesCleanupService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var cutoff = DateTime.UtcNow - _retention;
                var old = await db.ProcessedMessages.Where(p => p.ProcessedAtUtc < cutoff).ToListAsync(stoppingToken);
                if (old.Count > 0)
                {
                    db.ProcessedMessages.RemoveRange(old);
                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Cleaned up {Count} processed message records older than {Cutoff}", old.Count, cutoff);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up processed messages");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("ProcessedMessagesCleanupService stopped");
    }
}
