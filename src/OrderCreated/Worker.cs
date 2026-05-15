using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using Shared.Contracts.Events;
using Shared.Contracts.Enums;
using System.Text.Json;

namespace OrderCreated;

/// <summary>
/// Worker is a background service responsible for publishing pending outbox messages to the message bus. It periodically checks the OutboxMessages table for any messages that have not yet been published (where PublishedAtUtc is null) and attempts to publish them. The worker handles deserialization of the event payload, sends the message to the appropriate queue based on the event type, and updates the OutboxMessages record with the PublishedAtUtc timestamp upon successful publication. If an error occurs during publishing, it increments a retry count and logs the error for monitoring purposes. This ensures reliable delivery of messages even in the face of transient failures, while keeping the implementation straightforward and focused on its core responsibility of publishing outbox messages.
/// </summary>
/// <param name="logger">Logger used to report informational and error messages from the worker.</param>
/// <param name="scopeFactory">Service scope factory used to create scoped service providers for database access.</param>
/// <param name="bus">MassTransit bus used to obtain send endpoints for publishing events.</param>
/// <param name="config">Configuration used to read settings such as RabbitMQ queue names.</param>
public class Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory, IBus bus, IConfiguration config) : BackgroundService
{
    /// <summary>
    /// lastStatusUpdateUtc tracks the last time the worker logged a status update about pending messages. This is used to limit the frequency of logging when there are messages to publish, ensuring that we log at most once every 30 seconds about pending messages. This helps reduce log noise while still providing visibility into the worker's activity when there are messages to be published.
    /// </summary>
    private DateTime _lastStatusUpdateUtc = DateTime.MinValue;

    /// <summary>
    /// ExecuteAsync is the main method of the Worker background service. It runs an infinite loop that periodically checks for pending outbox messages to publish.
    /// For each pending message the worker attempts to deserialize the payload and publish it to the appropriate queue based on the <c>EventType</c>.
    /// On successful send the worker marks the outbox row with <c>PublishedAtUtc</c> so it will not be re-sent. On failures the worker increments a retry count
    /// and records the error so transient problems can be retried later. The loop sleeps between iterations and honors the provided cancellation token
    /// so the host can shut down gracefully.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token that signals the worker should stop processing and exit.</param>
    /// <returns>A task that represents the lifetime of the background worker; it completes when the worker stops.</returns>
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
                                // compute PublishedAtUtc now so we include it as a header during send
                                var publishedAt = DateTime.UtcNow;
                                // set PublishedAtUtc on the event payload for reliable consumer-side validation
                                evt.PublishedAtUtc = publishedAt;

                                await sendEndpoint.Send<OrderCreatedEvent>(evt, sendContext =>
                                {
                                    sendContext.MessageId = msg.Id;
                                    // include PublishedAtUtc so consumers can verify the message was published by the outbox
                                    try
                                    {
                                        sendContext.Headers.Set("PublishedAtUtc", publishedAt);
                                    }
                                    catch
                                    {
                                        // ignore header set failures - it's best effort
                                    }
                                }, stoppingToken);

                                // Mark this message as published and persist immediately.
                                msg.PublishedAtUtc = publishedAt;
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
                                // publish and record PublishedAtUtc so we don't re-send later
                                var publishedAt2 = DateTime.UtcNow;
                                await sendEndpoint2.Send<OrderStatusUpdatedEvent>(evt2, sendContext =>
                                {
                                    sendContext.MessageId = msg.Id;
                                    try
                                    {
                                        sendContext.Headers.Set("PublishedAtUtc", publishedAt2);
                                    }
                                    catch { }
                                }, stoppingToken);

                                // mark this outbox message as published
                                msg.PublishedAtUtc = publishedAt2;
                                msg.Error = null;
                                try
                                {
                                    await db.SaveChangesAsync(stoppingToken);
                                }
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
