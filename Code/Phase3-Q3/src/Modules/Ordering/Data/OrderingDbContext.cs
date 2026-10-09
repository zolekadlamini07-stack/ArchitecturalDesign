using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.Domain;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Ordering.Data;

internal sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<PlaceOrderRequest> PlaceOrderRequests => Set<PlaceOrderRequest>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("ordering");
        b.AddOutbox(); // Q2: this module's outbox table

        b.Entity<Order>(o =>
        {
            o.ToTable("orders");
            o.Property(x => x.Status).HasConversion<string>();
            o.Ignore(x => x.TotalAsMoney);
            // Lines and history belong to the aggregate: loaded and saved WITH the order.
            o.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId);
            o.HasMany(x => x.History).WithOne().HasForeignKey(h => h.OrderId);
            o.Navigation(x => x.Lines).HasField("_lines").AutoInclude();
            o.Navigation(x => x.History).HasField("_history");
        });
        // Ids are created in code (not by the database), so new history rows added to an existing
        // order are correctly treated as INSERTs.
        b.Entity<PlaceOrderRequest>(r => { r.ToTable("place_order_requests"); r.HasKey(x => new { x.CustomerId, x.IdempotencyKey }); });
        b.Entity<OrderLine>(l => { l.ToTable("order_lines"); l.Property(x => x.Id).ValueGeneratedNever(); });
        b.Entity<OrderStatusChange>(h =>
        {
            h.ToTable("order_status_history");
            h.Property(x => x.Id).ValueGeneratedNever();
            h.Property(x => x.FromStatus).HasConversion<string>();
            h.Property(x => x.ToStatus).HasConversion<string>();
        });
    }
}

/// <summary>Q3: remembers which order a customer's Idempotency-Key produced.</summary>
internal sealed class PlaceOrderRequest
{
    public Guid CustomerId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public Guid OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
