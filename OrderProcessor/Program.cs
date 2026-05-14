using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderProcessor;
using OrderService.Data;
using Microsoft.Extensions.Logging;
using OrderService.Utils;

var builder = Host.CreateApplicationBuilder(args);

// Register DbContext using DI so DbResolver can use the application's ILogger.
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    var cs = DbResolver.ResolveSqliteConnectionString(builder.Configuration, builder.Configuration.GetConnectionString("DefaultConnection"), builder.Environment.ContentRootPath, logger, allowPrepare: false);
    options.UseSqlite(cs);
});

var rabbitCfg = builder.Configuration.GetSection("RabbitMq");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();
    // Ensure MassTransit publish endpoint is available for the batcher

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

// Register batcher for DB updates
builder.Services.AddSingleton<OrderProcessingBatcher>();
// Register status advancer service to move Processing->Shipped->Delivered
builder.Services.AddHostedService<OrderStatusAdvancerService>();
// Register cleanup service for processed message dedup table
builder.Services.AddHostedService<ProcessedMessagesCleanupService>();
// Allow cleanup config from appsettings
builder.Services.AddOptions();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.Migrate();
    }
    catch (Exception)
    {
        // If applying migrations fails (for example during early development when the DB
        // is out-of-sync), fall back to EnsureCreated so missing tables are created.
        // This avoids the runtime Sqlite "no such table" error while still attempting
        // to apply migrations where possible.
        db.Database.EnsureCreated();
    }
}

// Ensure the OrderProcessingBatcher singleton is resolved so it starts its internal worker.
// The batcher starts its background loop in the constructor; resolving it here guarantees
// it will run even if no other component explicitly depends on it.
_ = host.Services.GetRequiredService<OrderProcessingBatcher>();

host.Run();
