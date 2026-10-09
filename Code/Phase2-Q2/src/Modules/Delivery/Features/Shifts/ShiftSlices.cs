using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Delivery.Data;
using FoodDelivery.Modules.Identity.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Delivery.Features.Shifts;

/// <summary>SLICE: "Driver signs in for a shift" (brief: Drivers - "Become available for deliveries").</summary>
internal static class StartShift
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/delivery/shifts/start", Handle).RequireAuthorization(p => p.RequireRole(Roles.Driver)).WithTags("Delivery");

    private static async Task<IResult> Handle(ClaimsPrincipal driver, DeliveryDbContext db, CancellationToken ct)
    {
        var driverId = driver.UserId();
        if (await db.Shifts.AnyAsync(s => s.DriverId == driverId && s.EndedAt == null, ct))
            return Results.NoContent(); // already on shift - starting again changes nothing

        db.Shifts.Add(new DriverShift { Id = Guid.NewGuid(), DriverId = driverId, StartedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

/// <summary>SLICE: "Driver signs out" (no longer receives delivery requests).</summary>
internal static class EndShift
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/delivery/shifts/end", Handle).RequireAuthorization(p => p.RequireRole(Roles.Driver)).WithTags("Delivery");

    private static async Task<IResult> Handle(ClaimsPrincipal driver, DeliveryDbContext db, CancellationToken ct)
    {
        var driverId = driver.UserId();
        await db.Shifts.Where(s => s.DriverId == driverId && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, DateTimeOffset.UtcNow), ct);
        return Results.NoContent();
    }
}

/// <summary>
/// EVENT HANDLER SLICE: "When a restaurant creates a driver login, create the driver's profile".
/// Subscribes to Identity's public DriverAccountCreated event.
/// </summary>
internal sealed class CreateDriverProfile(DeliveryDbContext db) : IDomainEventHandler<DriverAccountCreated>
{
    public async Task HandleAsync(DriverAccountCreated e, CancellationToken ct)
    {
        if (await db.Drivers.AnyAsync(d => d.Id == e.UserId, ct)) return; // safe to run twice
        db.Drivers.Add(new Driver { Id = e.UserId, RestaurantId = e.RestaurantId, DisplayName = e.DisplayName, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }
}
