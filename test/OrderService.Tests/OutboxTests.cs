using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Data;
using OrderService.Entities;
using OrderService.Services;
using Shared.Contracts.Events;
using Shared.Contracts.Enums;
using Xunit;

namespace OrderService.Tests;

public class OutboxTests
{
    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("test_db" + Guid.NewGuid()));
        services.AddScoped<OrderService.Services.OrderService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task UpdateOrderStatus_CreatesOutboxMessage()
    {
        var sp = BuildServices();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<OrderService.Services.OrderService>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            Status = OrderStatus.Processing,
            StatusUpdatedAtUtc = DateTime.UtcNow,
            Email = "test@example.com"
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var updated = await svc.UpdateOrderStatusAsync(order.Id, OrderStatus.Shipped, true);
        Assert.True(updated);

        var outbox = await db.OutboxMessages.FirstOrDefaultAsync();
        Assert.NotNull(outbox);
        var evt = JsonSerializer.Deserialize<OrderStatusUpdatedEvent>(outbox.Payload);
        Assert.Equal(order.Id, evt.OrderId);
        Assert.Equal(OrderStatus.Processing, evt.OldStatus);
        Assert.Equal(OrderStatus.Shipped, evt.NewStatus);
    }

    [Fact]
    public async Task CancelOrder_CreatesOutboxMessage()
    {
        var sp = BuildServices();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<OrderService.Services.OrderService>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            Status = OrderStatus.Pending,
            StatusUpdatedAtUtc = DateTime.UtcNow,
            Email = "test2@example.com"
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var cancelled = await svc.CancelOrderAsync(order.Id);
        Assert.True(cancelled);

        var outbox = await db.OutboxMessages.FirstOrDefaultAsync();
        Assert.NotNull(outbox);
        var evt = JsonSerializer.Deserialize<OrderStatusUpdatedEvent>(outbox.Payload);
        Assert.Equal(order.Id, evt.OrderId);
        Assert.Equal(OrderStatus.Pending, evt.OldStatus);
        Assert.Equal(OrderStatus.Cancelled, evt.NewStatus);
    }
}
