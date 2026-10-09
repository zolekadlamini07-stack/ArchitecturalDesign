using FoodDelivery.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Delivery.Data;

internal static class DeliveryStatus
{
    public const string Requested = "REQUESTED";
    public const string Accepted = "ACCEPTED";
    public const string PickedUp = "PICKED_UP";
    public const string Delivered = "DELIVERED";
}

internal sealed class Driver
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class DriverShift
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

internal sealed class DeliveryJob
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid? DriverId { get; set; }
    public string Status { get; set; } = DeliveryStatus.Requested;
    public string DeliveryAddress { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? PickedUpAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

/// <summary>
/// Note the word "DeliveryJob": in the Delivery module the thing a driver works on is a delivery
/// job, while Ordering calls the same real-world event an "order". Same event, different words in
/// different modules - that is what DDD means by separate BOUNDED CONTEXTS.
/// </summary>
internal sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : DbContext(options)
{
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<DriverShift> Shifts => Set<DriverShift>();
    public DbSet<DeliveryJob> Deliveries => Set<DeliveryJob>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("delivery");
        b.AddOutbox(); // Q2: this module's outbox table
        b.Entity<Driver>().ToTable("drivers");
        b.Entity<DriverShift>().ToTable("driver_shifts");
        b.Entity<DeliveryJob>().ToTable("deliveries");
    }
}
