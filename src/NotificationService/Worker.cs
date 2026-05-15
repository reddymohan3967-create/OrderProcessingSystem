using MassTransit;
using Shared.Contracts.Events;

namespace NotificationService;

public class OrderStatusUpdatedConsumer : IConsumer<OrderStatusUpdatedEvent>
{
    private readonly ILogger<OrderStatusUpdatedConsumer> _logger;

    public OrderStatusUpdatedConsumer(ILogger<OrderStatusUpdatedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<OrderStatusUpdatedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Received OrderStatusUpdatedEvent: OrderId={OrderId} NewStatus={NewStatus} Email={Email}", evt.OrderId, evt.NewStatus, evt.Email);
        // Intentionally minimal: consumer acknowledges the event and does not perform additional work here.
        return Task.CompletedTask;
    }
}

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification worker running");
        return Task.CompletedTask;
    }
}

