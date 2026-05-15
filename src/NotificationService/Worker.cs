using MassTransit;
using Shared.Contracts.Events;

namespace NotificationService;

/// <summary>
/// Consumer that handles <see cref="OrderStatusUpdatedEvent"/> messages and
/// sends notification emails to customers when their order status changes.
/// </summary>
public class OrderStatusUpdatedConsumer : IConsumer<OrderStatusUpdatedEvent>
{
    private readonly ILogger<OrderStatusUpdatedConsumer> _logger;
    private readonly IEmailSender _emailSender;

    /// <summary>
    /// Constructs an <see cref="OrderStatusUpdatedConsumer"/>.
    /// </summary>
    /// <param name="logger">Logger for informational and error messages.</param>
    /// <param name="emailSender">Email sender implementation used to deliver notifications.</param>
    public OrderStatusUpdatedConsumer(ILogger<OrderStatusUpdatedConsumer> logger, IEmailSender emailSender)
    {
        _logger = logger;
        _emailSender = emailSender;
    }

    /// <summary>
    /// Consumes the <see cref="OrderStatusUpdatedEvent"/>, logs the update and
    /// attempts to send an email notification to the recipient if an email address is provided.
    /// </summary>
    /// <param name="context">MassTransit consume context containing the event message.</param>
    public async Task Consume(ConsumeContext<OrderStatusUpdatedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Received OrderStatusUpdatedEvent: OrderId={OrderId} NewStatus={NewStatus} Email={Email}", evt.OrderId, evt.NewStatus, evt.Email);

        if (string.IsNullOrWhiteSpace(evt.Email))
        {
            _logger.LogWarning("Skipping email send for OrderId {OrderId} because no email is provided", evt.OrderId);
            return;
        }

        var baseSubject = evt.NewStatus switch
        {
            Shared.Contracts.Enums.OrderStatus.Processing => "Your order is being processed",
            Shared.Contracts.Enums.OrderStatus.Shipped => "Your order has shipped",
            Shared.Contracts.Enums.OrderStatus.Delivered => "Your order has been delivered",
            Shared.Contracts.Enums.OrderStatus.Cancelled => "Your order was cancelled",
            _ => "Order status updated"
        };

        var subject = $"{baseSubject} (Order {evt.OrderId})";

        // Build a simple HTML email with order details and a plain-text fallback
        var html = $@"<html>
  <head>
    <meta charset='utf-8' />
    <meta name='viewport' content='width=device-width,initial-scale=1' />
    <style>
      body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial; color:#222; }}
      .container {{ max-width:600px; margin:24px auto; padding:16px; border:1px solid #eee; border-radius:8px; }}
      .header {{ font-size:18px; font-weight:600; margin-bottom:12px; }}
      .muted {{ color:#666; font-size:13px; }}
      .table {{ width:100%; border-collapse:collapse; margin-top:12px; }}
      .table th, .table td {{ border:1px solid #e6e6e6; padding:8px; text-align:left; font-size:14px; }}
      .btn {{ display:inline-block; padding:10px 16px; background-color:#1e40af !important; color:#ffffff !important; text-decoration:none !important; border-radius:8px; margin-top:12px; font-weight:600; border: none; }}
    </style>
  </head>
  <body>
    <div class='container'>
      <div class='header'>Order {evt.OrderId} status update</div>
      <div class='muted'>Status changed from <strong>{evt.OldStatus}</strong> to <strong>{evt.NewStatus}</strong></div>
      <div class='muted' style='margin-top:8px'>Updated: {evt.UpdatedAtUtc:u}</div>

      <table class='table' role='presentation'>
        <tr><th>Order ID</th><td>{evt.OrderId}</td></tr>
        <tr><th>Previous status</th><td>{evt.OldStatus}</td></tr>
        <tr><th>Current status</th><td>{evt.NewStatus}</td></tr>
        <tr><th>Customer email</th><td>{evt.Email}</td></tr>
      </table>

      <p style='margin-top:12px'>
        You can view the full order details in the app by clicking the button below.
      </p>
      <a class='btn' href='https://localhost:5001/orders/{evt.OrderId}'>View order details</a>

      <p class='muted' style='margin-top:16px;font-size:12px'>If you did not expect this change, please contact support.</p>
    </div>
  </body>
</html>";

        var textFallback = $"Order {evt.OrderId} status changed from {evt.OldStatus} to {evt.NewStatus} at {evt.UpdatedAtUtc:u}.";

        try
        {
            await _emailSender.SendAsync(evt.Email, subject, html, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification email for OrderId {OrderId}", evt.OrderId);
        }
    }
}

/// <summary>
/// Minimal background worker that runs alongside MassTransit consumers. The
/// worker does not perform ongoing periodic work — it exists to keep the host
/// alive and provide a place for future background tasks if needed.
/// </summary>
public class Worker : BackgroundService
{
    /// <summary>
    /// Logger used to report worker lifecycle events.
    /// </summary>
    private readonly ILogger<Worker> _logger;

    /// <summary>
    /// Creates a new <see cref="Worker"/> instance.
    /// </summary>
    /// <param name="logger">Logger instance injected by DI.</param>
    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes the background worker. This implementation logs a start message
    /// and waits until the host shuts down. Override to add periodic background work.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token that signals host shutdown.</param>
    /// <returns>A task that completes when the worker stops.</returns>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification worker running");

        // Keep the worker alive until the host is stopped so MassTransit consumers
        // and other background activity continue processing. Await an infinite
        // delay that is cancelled when the host is shutting down.
        return Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }, stoppingToken);
    }
}

