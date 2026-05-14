using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderProcessor;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Events;

var builder = Host.CreateApplicationBuilder(args);

string ResolveSqliteConnectionString(string? connectionString, string contentRoot)
{
    if (string.IsNullOrEmpty(connectionString))
        throw new InvalidOperationException("DefaultConnection is not configured.");

    const string prefix = "Data Source=";
    if (!connectionString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return connectionString;

    var path = connectionString[prefix.Length..].Trim();
    if (Path.IsPathRooted(path))
        return connectionString;

    var resolved = Path.GetFullPath(Path.Combine(contentRoot, path));
    return $"Data Source={resolved}";
}

var resolvedConn = ResolveSqliteConnectionString(builder.Configuration.GetConnectionString("DefaultConnection"), builder.Environment.ContentRootPath);
// Print resolved connection so we can verify the exact DB file used at runtime
Console.WriteLine($"Using SQLite connection: {resolvedConn}");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(resolvedConn));

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

// Register batcher for DB updates
builder.Services.AddSingleton<OrderProcessingBatcher>();
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

host.Run();
