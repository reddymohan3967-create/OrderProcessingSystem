using NotificationService;
using MassTransit;
using Shared.Contracts.Events;

var builder = Host.CreateApplicationBuilder(args);
// Configure SMTP options from config
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));

// Prefer environment variables / user-secrets for sensitive SMTP settings
builder.Services.PostConfigure<SmtpOptions>(opts =>
{
    var envUser = Environment.GetEnvironmentVariable("Smtp__Username");
    var envPass = Environment.GetEnvironmentVariable("Smtp__Password");
    var envFrom = Environment.GetEnvironmentVariable("Smtp__From");

    if (!string.IsNullOrEmpty(envUser)) opts.Username = envUser;
    if (!string.IsNullOrEmpty(envPass)) opts.Password = envPass;
    if (!string.IsNullOrEmpty(envFrom)) opts.From = envFrom;
});

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
            e.ConfigureConsumer<OrderStatusUpdatedConsumer>(context);
        });
    });
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
