using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Delivery.Data;
using FoodDelivery.Modules.Delivery.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Delivery.Features.UpdateDeliveryStatus;

// Brief: Drivers - "update delivery status". Two slices: picked up, delivered.
// Each one saves its change together with a fact in the OUTBOX (Q2). Ordering listens and moves the order on.

/// <summary>SLICE: "I've collected the food".</summary>
internal static class MarkPickedUp
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/delivery/{deliveryId:guid}/picked-up", Handle).RequireAuthorization(p => p.RequireRole(Roles.Driver)).WithTags("Delivery");

    private static async Task<IResult> Handle(Guid deliveryId, ClaimsPrincipal driver, DeliveryDbContext db, CancellationToken ct)
    {
        var job = await db.Deliveries.SingleOrDefaultAsync(d => d.Id == deliveryId && d.DriverId == driver.UserId(), ct);
        if (job is null) return Problems.NotFound("Delivery");
        if (job.Status != DeliveryStatus.Accepted) return Problems.Conflict($"Cannot pick up a delivery that is {job.Status}.");

        job.Status = DeliveryStatus.PickedUp;
        job.PickedUpAt = DateTimeOffset.UtcNow;
        // Q2: the event is saved WITH the status change (outbox). Ordering's handler runs in the
        // Worker a moment later - and if anything crashes, the event is still safely stored.
        db.AddToOutbox(new DeliveryPickedUp(job.OrderId, job.DriverId!.Value));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

/// <summary>SLICE: "I've handed the food to the customer".</summary>
internal static class MarkDelivered
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/delivery/{deliveryId:guid}/delivered", Handle).RequireAuthorization(p => p.RequireRole(Roles.Driver)).WithTags("Delivery");

    private static async Task<IResult> Handle(Guid deliveryId, ClaimsPrincipal driver, DeliveryDbContext db, CancellationToken ct)
    {
        var job = await db.Deliveries.SingleOrDefaultAsync(d => d.Id == deliveryId && d.DriverId == driver.UserId(), ct);
        if (job is null) return Problems.NotFound("Delivery");
        if (job.Status != DeliveryStatus.PickedUp) return Problems.Conflict($"Cannot deliver a delivery that is {job.Status}.");

        job.Status = DeliveryStatus.Delivered;
        job.DeliveredAt = DateTimeOffset.UtcNow;
        db.AddToOutbox(new DeliveryCompleted(job.OrderId, job.DriverId!.Value));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

/// <summary>SLICE: "My current and previous deliveries" (paginated).</summary>
internal static class ListMyDeliveries
{
    public sealed record Item(Guid DeliveryId, Guid OrderId, string Status, string DeliveryAddress, DateTimeOffset RequestedAt, DateTimeOffset? DeliveredAt);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/delivery/mine", Handle).RequireAuthorization(p => p.RequireRole(Roles.Driver)).WithTags("Delivery");

    private static async Task<IResult> Handle(ClaimsPrincipal driver, DeliveryDbContext db, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var driverId = driver.UserId();
        var items = await db.Deliveries.AsNoTracking()
            .Where(d => d.DriverId == driverId)
            .OrderByDescending(d => d.RequestedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new Item(d.Id, d.OrderId, d.Status, d.DeliveryAddress, d.RequestedAt, d.DeliveredAt))
            .ToListAsync(ct);
        return Results.Ok(new Page<Item>(items, page, pageSize));
    }
}
