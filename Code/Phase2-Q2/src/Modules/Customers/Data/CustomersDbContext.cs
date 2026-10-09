using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Customers.Data;

internal sealed class CustomerProfile
{
    public Guid CustomerId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Phone { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class Address
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Line1 { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public bool IsDefault { get; set; }
}

/// <summary>Customers' own DbContext - it can only see the 'customers' schema.</summary>
internal sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options) : DbContext(options)
{
    public DbSet<CustomerProfile> Profiles => Set<CustomerProfile>();
    public DbSet<Address> Addresses => Set<Address>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("customers");
        b.Entity<CustomerProfile>().ToTable("customer_profiles").HasKey(p => p.CustomerId);
        b.Entity<Address>().ToTable("addresses");
    }
}
