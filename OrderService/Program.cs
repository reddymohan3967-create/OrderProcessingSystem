using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Interfaces;
using OrderService.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        // Serialize enums as strings so Swagger shows names instead of numbers
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Ensure the API uses the project-root orders.db file (absolute path) so it's consistent
// regardless of working directory when running from the IDE.
// Resolve the SQLite connection string from configuration and convert any relative Data Source
string ResolveSqliteConnectionString(string? connectionString, string contentRoot)
{
    if (string.IsNullOrEmpty(connectionString))
        throw new InvalidOperationException("DefaultConnection is not configured.");

    // Expect format: "Data Source=path"
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

// Register EF-backed order service after DbContext registration
builder.Services.AddScoped<IOrderService, OrderService.Services.OrderService>();

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
