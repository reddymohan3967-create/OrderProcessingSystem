using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
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
    options.OnRejected = (context, cancellationToken) =>
    {
        // set Retry-After header to advise clients and return a small JSON body
        context.HttpContext.Response.Headers["Retry-After"] = "60";
        context.HttpContext.Response.ContentType = "application/json";
        var body = System.Text.Json.JsonSerializer.Serialize(new {
            message = "Too many requests. Try again in 60 seconds.",
            retryAfter = 60
        });
        return new System.Threading.Tasks.ValueTask(context.HttpContext.Response.WriteAsync(body, cancellationToken));
    };

    // partition by remote IP so each IP gets its own limiter with PermitLimit properly set
    options.AddPolicy("PerIpPolicy", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

// Resolve DB path relative to content root so the DB file can be kept in repo under /data/orders.db
var conn = OrderService.Utils.DbResolver.ResolveSqliteConnectionString(builder.Configuration, null, builder.Environment.ContentRootPath, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, true);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(conn));

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
