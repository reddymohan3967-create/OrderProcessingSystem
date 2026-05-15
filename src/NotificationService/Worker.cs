using MassTransit;
using Shared.Contracts.Events;

namespace NotificationService;

/// <summary>
/// OrderStatusUpdatedConsumer listens for OrderStatusUpdatedEvent messages and logs the status updates. This consumer is intentionally minimal, serving primarily to acknowledge receipt of the event and log its contents for demonstration purposes. In a real application, this could be extended to trigger email notifications or other side effects based on the order status change.
/// </summary>
public class OrderStatusUpdatedConsumer : IConsumer<OrderStatusUpdatedEvent>
{
    private readonly ILogger<OrderStatusUpdatedConsumer> _logger;
    private readonly IEmailSender _emailSender;

    public OrderStatusUpdatedConsumer(ILogger<OrderStatusUpdatedConsumer> logger, IEmailSender emailSender)
    {
        _logger = logger;
        _emailSender = emailSender;
    }

    public async Task Consume(ConsumeContext<OrderStatusUpdatedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation("Received OrderStatusUpdatedEvent: OrderId={OrderId} NewStatus={NewStatus} Email={Email}", evt.OrderId, evt.NewStatus, evt.Email);

        if (string.IsNullOrWhiteSpace(evt.Email))
        {
            _logger.LogWarning("Skipping email send for OrderId {OrderId} because no email is provided", evt.OrderId);
            return;
        }

        var subject = evt.NewStatus switch
        {
            Shared.Contracts.Enums.OrderStatus.Processing => "Your order is being processed",
            Shared.Contracts.Enums.OrderStatus.Shipped => "Your order has shipped",
            Shared.Contracts.Enums.OrderStatus.Delivered => "Your order has been delivered",
            Shared.Contracts.Enums.OrderStatus.Cancelled => "Your order was cancelled",
            _ => "Order status updated"
        };

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
/// Worker is a background service that runs alongside the MassTransit consumers. In this minimal implementation, it simply logs a message when it starts and does not perform any ongoing work. This allows us to verify that the worker is running and can be extended in the future to include additional background processing if needed.
/// </summary>
public class Worker : BackgroundService
{
    /// <summary>
    /// Worker uses an ILogger to log information about its execution. In this minimal implementation, it logs a message when the worker starts running. The logger is injected via the constructor and is used to provide visibility into the worker's activity, even though the worker itself does not perform any significant processing in this example.
    /// </summary>
    private readonly ILogger<Worker> _logger;

    /// <summary>
    /// Worker singleton constructor takes an ILogger<Worker> which is injected by the dependency injection container. This logger is used to log information about the worker's execution, such as when it starts running. The constructor does not perform any additional initialization, keeping the worker focused on its primary responsibility of running in the background and logging its activity.
    /// </summary>
    /// <param name="logger"></param>
    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// ExecuteAsync is the main method of the Worker background service. In this minimal implementation, it logs a message indicating that the notification worker is running and then completes immediately. This allows us to verify that the worker is executing as expected without introducing any additional complexity or ongoing processing in this example.
    /// </summary>
    /// <param name="stoppingToken"></param>
    /// <returns></returns>
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

