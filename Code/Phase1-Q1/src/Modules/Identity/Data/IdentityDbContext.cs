using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Identity.Data;

/// <summary>A user account. 'internal' - no other module can see this class.</summary>
internal sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public Guid? RestaurantId { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// The Identity module's own DbContext, mapped to the 'identity' schema only.
/// Each module has its OWN DbContext - there is no "one big context" that sees every table,
/// which would make it far too easy to reach into another module's data.
/// </summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("identity");
        b.Entity<User>().ToTable("users");
    }
}
