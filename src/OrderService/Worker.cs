using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using OrderService.Data;
using Shared.Contracts.Events;
using System.Text.Json;

namespace OrderService;

/// <summary>
/// Background worker that publishes outbox messages for the OrderService.
/// The worker periodically polls the OutboxMessages table and publishes
/// events to the configured message bus, marking messages as published on success.
/// </summary>
/// <param name="logger">Logger used to report status and errors.</param>
/// <param name="scopeFactory">Service scope factory used to create scoped DB contexts.</param>
/// <param name="bus">MassTransit bus used to obtain endpoints for sending events.</param>
/// <param name="config">Configuration used to read RabbitMQ settings and queue names.</param>
public class Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory, IBus bus, IConfiguration config) : BackgroundService
{
    private DateTime _lastStatusUpdateUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox Publisher Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var pending = await db.OutboxMessages
                    .Where(m => m.PublishedAtUtc == null)
                    .OrderBy(m => m.CreatedAtUtc)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                if (pending.Count > 0)
                    logger.LogInformation("Outbox worker publishing {Count} pending messages", pending.Count);

                foreach (var msg in pending)
                {
                    try
                    {
                        if (msg.EventType == nameof(OrderCreatedEvent))
                        {
                            var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(msg.Payload);
                            if (evt != null)
                            {
                                var queueName = Environment.GetEnvironmentVariable("RABBITMQ_QUEUE")
                                    ?? config?["RabbitMq:Queue"]
                                    ?? "order-created-queue";

                                var sendEndpoint = await bus.GetSendEndpoint(new Uri($"queue:{queueName}"));
                                var publishedAt = DateTime.UtcNow;
                                evt.PublishedAtUtc = publishedAt;

                                await sendEndpoint.Send<OrderCreatedEvent>(evt, sendContext =>
                                {
                                    sendContext.MessageId = msg.Id;
                                    try { sendContext.Headers.Set("PublishedAtUtc", publishedAt); } catch { }
                                }, stoppingToken);

                                msg.PublishedAtUtc = publishedAt;
                                msg.Error = null;
                                try { await db.SaveChangesAsync(stoppingToken); }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "Failed to persist PublishedAtUtc for message {MessageId}", msg.Id);
                                }
                                continue;
                            }
                            else
                            {
                                logger.LogWarning("Failed to deserialize outbox payload for message {MessageId}", msg.Id);
                            }
                        }
                        else if (msg.EventType == nameof(OrderStatusUpdatedEvent))
                        {
                            var evt2 = JsonSerializer.Deserialize<OrderStatusUpdatedEvent>(msg.Payload);
                            if (evt2 != null)
                            {
                                var queueName2 = Environment.GetEnvironmentVariable("RABBITMQ_QUEUE_STATUS")
                                    ?? config?["RabbitMq:QueueStatus"]
                                    ?? "order-status-updates";

                                var sendEndpoint2 = await bus.GetSendEndpoint(new Uri($"queue:{queueName2}"));
                                var publishedAt2 = DateTime.UtcNow;
                                await sendEndpoint2.Send<OrderStatusUpdatedEvent>(evt2, sendContext =>
                                {
                                    sendContext.MessageId = msg.Id;
                                    try { sendContext.Headers.Set("PublishedAtUtc", publishedAt2); } catch { }
                                }, stoppingToken);

                                msg.PublishedAtUtc = publishedAt2;
                                msg.Error = null;
                                try { await db.SaveChangesAsync(stoppingToken); }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "Failed to persist PublishedAtUtc for status message {MessageId}", msg.Id);
                                }
                            }
                            else
                            {
                                logger.LogWarning("Failed to deserialize outbox payload for status message {MessageId}", msg.Id);
                            }
                        }
                        else
                        {
                            // unknown event type - ignore
                        }
                    }
                    catch (Exception ex)
                    {
                        msg.RetryCount += 1;
                        msg.Error = ex.Message;
                        logger.LogError(ex, "Failed to publish outbox message {MessageId}, retry count: {RetryCount}", msg.Id, msg.RetryCount);
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox worker failure");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        logger.LogInformation("Outbox Publisher Worker stopped");
    }
}
