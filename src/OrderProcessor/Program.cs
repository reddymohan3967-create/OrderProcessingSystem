using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderProcessor;
using OrderService.Data;

var builder = Host.CreateApplicationBuilder(args);

// Resolve DB path so the DB file lives in repository under /data/orders.db by default
// In some environments the content root for worker projects points to the project's bin
// directory. ResolveSqliteConnectionString walks up parent directories to find a
// repository-level `data/orders.db`, so pass the content root and allowPrepare=false
// to avoid creating files from worker processes. If callers want creation behavior,
// they can call with allowPrepare=true from a designated host process.
var conn = OrderService.Utils.DbResolver.ResolveSqliteConnectionString(builder.Configuration, null, builder.Environment.ContentRootPath, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, true);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(conn));

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
builder.Services.AddHostedService<ProcessedMessagesCleanupService>();
builder.Services.AddOptions();

var host = builder.Build();

// Ensure the in-memory batcher is created so it can begin seeding from persisted work and start background processing.
_ = host.Services.GetRequiredService<OrderProcessingBatcher>();

host.Run();
