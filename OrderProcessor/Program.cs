using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderProcessor;
using OrderService.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=\"C:\\ProgramData\\OrderProcessing\\orders.db\""));

var rabbitCfg = builder.Configuration.GetSection("RabbitMq");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitCfg["Host"] ?? "localhost", h =>
        {
            h.Username(rabbitCfg["Username"] ?? "guest");
            h.Password(rabbitCfg["Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint(rabbitCfg["Queue"] ?? "order-created-queue", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = 16;
            e.ConfigureConsumer<OrderCreatedConsumer>(context);
        });
    });
});

builder.Services.AddSingleton<OrderProcessingBatcher>();
builder.Services.AddHostedService<OrderStatusAdvancerService>();
builder.Services.AddHostedService<ProcessedMessagesCleanupService>();
builder.Services.AddOptions();

var host = builder.Build();

_ = host.Services.GetRequiredService<OrderProcessingBatcher>();

host.Run();
