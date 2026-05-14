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

                logger.LogInformation("Outbox worker found {Count} pending messages (DB: {DataSource})", pending.Count, db.Database.GetDbConnection().DataSource);

                foreach (var msg in pending)
                {
                    logger.LogInformation("Processing outbox message {MessageId} ({EventType})", msg.Id, msg.EventType);
                    logger.LogDebug("Outbox payload for {MessageId}: {Payload}", msg.Id, msg.Payload);
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
                            }
                            else
                            {
                                logger.LogWarning("Failed to deserialize outbox payload for message {MessageId}", msg.Id);
                            }
                        }
                        else
                        {
                            logger.LogDebug("Skipping unknown event type {EventType} for message {MessageId}", msg.EventType, msg.Id);
                        }

                        msg.PublishedAtUtc = DateTime.UtcNow;
                        msg.Error = null;

                        // Published successfully; append a lightweight publish log entry if configured
                        try
                        {
                            var publishLogPath = Environment.GetEnvironmentVariable("PUBLISH_LOG_PATH")
                                ?? config?["PublishLogPath"];

                            if (!string.IsNullOrEmpty(publishLogPath))
                            {
                                var resolvedLogFile = Path.IsPathRooted(publishLogPath)
                                    ? publishLogPath
                                    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, publishLogPath));

                                Directory.CreateDirectory(Path.GetDirectoryName(resolvedLogFile)!);
                                var publishEntry = new
                                {
                                    MessageId = msg.Id,
                                    EventType = msg.EventType,
                                    PublishedAtUtc = msg.PublishedAtUtc
                                };

                                var line = JsonSerializer.Serialize(publishEntry);
                                await File.AppendAllTextAsync(resolvedLogFile, line + Environment.NewLine, stoppingToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to write publish log for outbox message {MessageId}", msg.Id);
                        }

                        logger.LogInformation("Published outbox message {MessageId} ({EventType})", msg.Id, msg.EventType);
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
