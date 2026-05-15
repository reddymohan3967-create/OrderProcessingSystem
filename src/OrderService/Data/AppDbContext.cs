using Microsoft.EntityFrameworkCore;
using OrderService.Entities;

namespace OrderService.Data;

/// <summary>
/// EF Core DB context for the OrderService domain. Contains DbSets for orders,
/// outbox messages and background processing markers used throughout the application.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    /// <summary>Orders placed by customers.</summary>
    public DbSet<Order> Orders => Set<Order>();
    /// <summary>Individual order line items.</summary>
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    /// <summary>Outbox messages for reliable event publication.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    /// <summary>Processed message markers used for idempotency.</summary>
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    /// <summary>Durable pending work rows used by the batcher.</summary>
    public DbSet<PendingWork> PendingWork => Set<PendingWork>();
    /// <summary>Product catalog entries.</summary>
    public DbSet<Entities.Product> Products => Set<Entities.Product>();
    /// <summary>Application users for basic authentication.</summary>
    public DbSet<Entities.AppUser> AppUsers => Set<Entities.AppUser>();

    /// <summary>
    /// 
    /// </summary>
    /// <param name="modelBuilder"></param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.Id);

            entity.Property(o => o.TotalAmount)
                  .HasPrecision(18, 2);

            entity.Property(o => o.Email)
                  .IsRequired()
                  .HasMaxLength(255);

            entity.HasMany(o => o.Items)
                  .WithOne(i => i.Order)
                  .HasForeignKey(i => i.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(i => i.Id);

            entity.Property(i => i.UnitPrice)
                  .HasPrecision(18, 2);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(o => o.Id);
        });

        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => p.Id).IsUnique();
        });

        modelBuilder.Entity<PendingWork>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => p.OrderId).IsUnique();
        });
    }
}
