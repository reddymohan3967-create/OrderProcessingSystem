using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderCreated;
using OrderService.Data;
using OrderService.Utils;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=\"C:\\ProgramData\\OrderProcessing\\orders.db\""));

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
