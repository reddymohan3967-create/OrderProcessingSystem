using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderCreated;
using OrderService.Data;
using OrderService.Utils;

var builder = Host.CreateApplicationBuilder(args);

// Resolve DB path so the DB file lives in repository under /data/orders.db by default
var conn = OrderService.Utils.DbResolver.ResolveSqliteConnectionString(builder.Configuration, null, builder.Environment.ContentRootPath, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, true);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(conn));

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

host.Run();
