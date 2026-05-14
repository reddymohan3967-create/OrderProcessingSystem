using OrderCreated;
using MassTransit;
using Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// Resolve connection string similarly to the API so configuration can provide relative paths
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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(resolvedConn));

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
