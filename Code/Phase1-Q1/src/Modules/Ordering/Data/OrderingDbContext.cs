using FoodDelivery.Modules.Ordering.Domain;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Ordering.Data;

internal sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("ordering");

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
