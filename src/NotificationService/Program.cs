using MassTransit;
using NotificationService;
using Microsoft.Extensions.Configuration;

var builder = Host.CreateApplicationBuilder(args);

// Configure MassTransit to listen for OrderStatusUpdatedEvent
var rabbit = builder.Configuration.GetSection("RabbitMq");
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderStatusUpdatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbit["Host"] ?? "localhost", h =>
        {
            h.Username(rabbit["Username"] ?? "guest");
            h.Password(rabbit["Password"] ?? "guest");
        });

        // Allow queue override from either RabbitMq:QueueStatus or legacy RabbitMq:Queue
        var queueName = Environment.GetEnvironmentVariable("RABBITMQ_QUEUE_STATUS")
            ?? rabbit["QueueStatus"]
            ?? rabbit["Queue"]
            ?? "order-status-updates";

        cfg.ReceiveEndpoint(queueName, e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = 16;
            e.ConfigureConsumer<OrderStatusUpdatedConsumer>(context);
        });
    });
});

// Keep Worker as a simple host; consumer handles incoming events
builder.Services.AddHostedService<Worker>();

// Register email sender
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

var host = builder.Build();
host.Run();
