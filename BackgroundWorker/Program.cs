using BackgroundWorker;
using MassTransit;
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

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"], h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"]);
            h.Password(builder.Configuration["RabbitMq:Password"]);
        });
    });
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Apply EF Core migrations on startup so the SQLite database and tables (including OutboxMessages)
// are created before the background worker starts processing.
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

host.Run();
