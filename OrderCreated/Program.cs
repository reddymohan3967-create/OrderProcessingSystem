using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderCreated;
using OrderService.Data;
using OrderService.Utils;

var builder = Host.CreateApplicationBuilder(args);

// Resolve connection string using the shared DbResolver so all projects point to the same DB location.
// Resolve DB path using DI-friendly logger once host services are available
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    var cs = DbResolver.ResolveSqliteConnectionString(builder.Configuration, builder.Configuration.GetConnectionString("DefaultConnection"), builder.Environment.ContentRootPath, logger, allowPrepare: false);
    options.UseSqlite(cs);
});

// read config
var rabbitCfg = builder.Configuration.GetSection("RabbitMq");

builder.Services.AddMassTransit(x =>
{
    // Publisher-only MassTransit configuration. No consumers are registered here.
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitCfg["Host"] ?? "localhost", h =>
        {
            h.Username(rabbitCfg["Username"] ?? "guest");
            h.Password(rabbitCfg["Password"] ?? "guest");
        });
    });
});

builder.Services.AddHostedService<Worker>();
// This is publisher-only service; consumer and batcher run in the separate OrderProcessor service.

var host = builder.Build();

// Apply EF Core migrations on startup so the SQLite database and tables (including OutboxMessages)
// are created before the background worker starts processing.
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

host.Run();
