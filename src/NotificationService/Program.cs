using MassTransit;
using NotificationService;

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

        cfg.ReceiveEndpoint(rabbit["Queue"] ?? "order-status-updates", e =>
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

var host = builder.Build();
host.Run();
