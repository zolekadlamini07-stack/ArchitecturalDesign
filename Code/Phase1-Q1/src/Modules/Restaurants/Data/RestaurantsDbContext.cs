using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Restaurants.Data;

internal sealed class Restaurant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal DeliveryFee { get; set; }
    public string Currency { get; set; } = "ZAR";
    public bool IsOpen { get; set; }
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
        b.Entity<Restaurant>().ToTable("restaurants");
        // Each menu item belongs to a restaurant. Telling EF Core this lets it save a NEW restaurant
        // before its NEW items when both are added together (e.g. a partner restaurant sync).
        b.Entity<MenuItem>(i => { i.ToTable("menu_items"); i.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId); });
    }
}
