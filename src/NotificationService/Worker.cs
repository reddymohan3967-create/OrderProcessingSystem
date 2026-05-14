using MassTransit;
using Microsoft.Extensions.Options;
using Shared.Contracts.Events;
using MailKit.Net.Smtp;
using MimeKit;

namespace NotificationService;

public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}

public class OrderStatusUpdatedConsumer : IConsumer<OrderStatusUpdatedEvent>
{
    private readonly ILogger<OrderStatusUpdatedConsumer> _logger;
    private readonly SmtpOptions _opts;

    public OrderStatusUpdatedConsumer(ILogger<OrderStatusUpdatedConsumer> logger, IOptions<SmtpOptions> opts)
    {
        _logger = logger;
        _opts = opts.Value;
    }

    public async Task Consume(ConsumeContext<OrderStatusUpdatedEvent> context)
    {
        var evt = context.Message;

        _logger.LogInformation("Received OrderStatusUpdatedEvent for {OrderId} from {Old} to {New} (email: {Email})", evt.OrderId, evt.OldStatus, evt.NewStatus, evt.Email);

        var recipient = evt.Email;
        if (string.IsNullOrWhiteSpace(recipient))
        {
            recipient = Environment.GetEnvironmentVariable("NOTIFICATION_RECIPIENT") ?? _opts.From;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_opts.From));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = $"Order {evt.OrderId} status: {evt.NewStatus}";
        message.Body = new TextPart("plain") { Text = $"Your order {evt.OrderId} is now {evt.NewStatus} (previous: {evt.OldStatus})." };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_opts.Host, _opts.Port, _opts.UseStartTls ? MailKit.Security.SecureSocketOptions.StartTls : MailKit.Security.SecureSocketOptions.Auto);
            if (!string.IsNullOrEmpty(_opts.Username))
            {
                await client.AuthenticateAsync(_opts.Username, _opts.Password);
            }
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Sent notification email for Order {OrderId} to {Recipient}", evt.OrderId, recipient);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification email for Order {OrderId}", evt.OrderId);
        }
    }
}

public class Worker : BackgroundService
{
    private readonly IBusControl _bus;
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger, IBusControl bus)
    {
        _logger = logger;
        _bus = bus;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification worker started");
        return Task.CompletedTask;
    }
}
