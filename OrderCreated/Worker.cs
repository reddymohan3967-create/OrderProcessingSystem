using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using Shared.Contracts.Events;
using Shared.Contracts.Enums;
using System.Text.Json;

namespace OrderCreated;

public class Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory, IBus bus, IConfiguration config) : BackgroundService
{
    private DateTime _lastStatusUpdateUtc = DateTime.MinValue;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox Publisher Worker started");

        // Simple outbox publisher loop
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

                // Minimal logging: only note when there are messages to publish
                if (pending.Count > 0)
                    logger.LogInformation("Outbox worker publishing {Count} pending messages", pending.Count);

                foreach (var msg in pending)
                {
                    // attempt publish for each pending outbox message
                    try
                    {
                        // Deserialize and publish typed event
                        if (msg.EventType == nameof(OrderCreatedEvent))
                        {

                            var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(msg.Payload);
                            if (evt != null)
                            {
                                // Determine queue name from environment or configuration so it's
                                // consistent with the RabbitMq:Queue setting in appsettings.
                                var queueName = Environment.GetEnvironmentVariable("RABBITMQ_QUEUE")
                                    ?? config?["RabbitMq:Queue"]
                                    ?? "order-created-queue";

                                var sendEndpoint = await bus.GetSendEndpoint(new Uri($"queue:{queueName}"));
                                await sendEndpoint.Send<OrderCreatedEvent>(evt, sendContext =>
                                {
                                    sendContext.MessageId = msg.Id;
                                }, stoppingToken);

                                // Mark this message as published and persist immediately.
                                msg.PublishedAtUtc = DateTime.UtcNow;
                                msg.Error = null;
                                try
                                {
                                    await db.SaveChangesAsync(stoppingToken);
                                }
                                catch (Exception ex)
                                {
                                    // Only warn on failure to persist the flag
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
                                await sendEndpoint2.Send<OrderStatusUpdatedEvent>(evt2, sendContext =>
                                {
                                    sendContext.MessageId = msg.Id;
                                }, stoppingToken);
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

                        // Failed to publish; error and retry count already set on message
                    }
                }

                // Persist Outbox message PublishedAtUtc / Error changes so messages are
                // marked published and won't be re-sent.
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
