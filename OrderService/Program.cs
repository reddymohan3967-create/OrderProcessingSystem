using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using Microsoft.AspNetCore.Authentication;
using OrderService.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Very small and explicit Program: register controllers, DB and cookie auth
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=\"C:\\ProgramData\\OrderProcessing\\orders.db\""));

// Register application services
builder.Services.AddScoped<OrderService.Services.OrderService>();
builder.Services.AddScoped<OrderService.Interfaces.IOrderService>(sp => sp.GetRequiredService<OrderService.Services.OrderService>());


builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "Basic";
    options.DefaultChallengeScheme = "Basic";
})
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BasicAuthenticationHandler>("Basic", null);

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
