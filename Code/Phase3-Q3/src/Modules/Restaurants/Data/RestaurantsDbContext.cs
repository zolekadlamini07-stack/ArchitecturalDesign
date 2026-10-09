using FoodDelivery.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Restaurants.Data;

internal static class ApprovalStatus
{
    public const string PendingApproval = "PENDING_APPROVAL";
    public const string Active = "ACTIVE";
}

internal sealed class Restaurant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal DeliveryFee { get; set; }
    public string Currency { get; set; } = "ZAR";
    public bool IsOpen { get; set; }
    public string ApprovalStatus { get; set; } = Data.ApprovalStatus.Active;
    public string? ContactEmail { get; set; }
    /// <summary>Q3: city as data. 3 cities today, 13 in two years - no code change per city.</summary>
    public string City { get; set; } = "Cape Town";
    /// <summary>Challenge: PARTNER rows are read-only copies owned by the acquired company's system.</summary>
    public string Source { get; set; } = "OWN";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class MenuItem
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; } = true;
}

internal sealed class RestaurantsDbContext(DbContextOptions<RestaurantsDbContext> options) : DbContext(options)
{
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("restaurants");
        b.AddOutbox(); // Q2: publishes RestaurantApproved
        b.Entity<Restaurant>().ToTable("restaurants");
        // Each menu item belongs to a restaurant. Telling EF Core this lets it save a NEW restaurant
        // before its NEW items when both are added together (e.g. a partner restaurant sync).
        b.Entity<MenuItem>(i => { i.ToTable("menu_items"); i.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId); });
    }
}
