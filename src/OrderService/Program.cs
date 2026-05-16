using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using MassTransit;
using OrderService.Data;
using Microsoft.AspNetCore.Authentication;
using OrderService.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Very small and explicit Program: register controllers, DB and cookie auth
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Rate limiting: per-IP fixed window (60 requests per minute)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    // Read rate limit settings from configuration if present
    var rateCfg = builder.Configuration.GetSection("RateLimiting");
    var permitLimit = rateCfg.GetValue<int?>("PermitLimit") ?? 600; // default increased to 600
    var windowSeconds = rateCfg.GetValue<int?>("WindowSeconds") ?? 60;

    options.OnRejected = (context, cancellationToken) =>
    {
        // set Retry-After header to advise clients and return a small JSON body
        context.HttpContext.Response.Headers["Retry-After"] = windowSeconds.ToString();
        context.HttpContext.Response.ContentType = "application/json";
        var body = System.Text.Json.JsonSerializer.Serialize(new {
            message = $"Too many requests. Try again in {windowSeconds} seconds.",
            retryAfter = windowSeconds
        });
        return new System.Threading.Tasks.ValueTask(context.HttpContext.Response.WriteAsync(body, cancellationToken));
    };

    // partition by remote IP so each IP gets its own limiter with PermitLimit properly set
    options.AddPolicy("PerIpPolicy", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

// Resolve DB path relative to content root so the DB file can be kept in repo under /data/orders.db
var conn = OrderService.Utils.DbResolver.ResolveSqliteConnectionString(builder.Configuration, null, builder.Environment.ContentRootPath, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, true);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(conn));

// Configure RabbitMQ (MassTransit) for publishing outbox messages. Uses config section RabbitMq or environment variables.
var rabbitCfg = builder.Configuration.GetSection("RabbitMq");
builder.Services.AddMassTransit(x =>
{
    // Publisher-only configuration; consumers run in other services
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitCfg["Host"] ?? "localhost", h =>
        {
            h.Username(rabbitCfg["Username"] ?? "guest");
            h.Password(rabbitCfg["Password"] ?? "guest");
        });
    });
});

// Register outbox publisher worker which will read OutboxMessages and publish to RabbitMQ
builder.Services.AddHostedService<OrderService.Worker>();

// In-memory caching and supporting services (minimal setup)
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<OrderService.Services.CacheManager>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var orderTtl = cfg.GetValue<int?>("CacheSettings:OrderTtlSeconds") ?? 30;
    var ordersListTtl = cfg.GetValue<int?>("CacheSettings:OrdersListTtlSeconds") ?? 15;
    return new OrderService.Services.CacheManager(orderTtl, ordersListTtl);
});
builder.Services.AddSingleton<OrderService.Services.CacheMetrics>();

// Create a temporary service provider to log cache configuration via the app logger
// so cache settings are visible at startup/runtime.
using (var _sp = builder.Services.BuildServiceProvider())
{
    try
    {
        var cm = _sp.GetRequiredService<OrderService.Services.CacheManager>();
        var lf = _sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
        var cacheLogger = lf.CreateLogger("Cache");
        cacheLogger.LogInformation("In-memory cache enabled. OrderTtl={OrderTtl}s, OrdersListTtl={OrdersListTtl}s", cm.GetOrderTtlSeconds(), cm.GetOrdersListTtlSeconds());
    }
    catch { }
}

// Register application services
builder.Services.AddScoped<OrderService.Services.OrderService>();
builder.Services.AddScoped<OrderService.Interfaces.IOrderService>(sp => sp.GetRequiredService<OrderService.Services.OrderService>());

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "Basic";
    options.DefaultChallengeScheme = "Basic";
})
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("Basic", null);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", p => p.RequireRole("Admin"));
    options.AddPolicy("RequireCustomer", p => p.RequireRole("Customer"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Apply rate limiter middleware (controllers opt-in via attribute)
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
