using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Delivery.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Delivery.Features.AcceptDelivery;

/// <summary>
/// SLICE: "Driver accepts a delivery" (brief: Drivers - "accept a delivery").
///
/// FIRST TO ACCEPT WINS. Two drivers may tap "accept" at the same moment. We don't read-then-write
/// (that has a race). We do ONE conditional UPDATE: "set me as driver WHERE it is still REQUESTED".
/// The database guarantees only one of them can change that row; the other gets 0 rows and a 409.
/// No locks, no matching algorithm - this is why restaurant-owned drivers keep Q1 simple (D7).
/// </summary>
internal static class AcceptDelivery
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/delivery/{deliveryId:guid}/accept", Handle).RequireAuthorization(p => p.RequireRole(Roles.Driver)).WithTags("Delivery");

    private static async Task<IResult> Handle(Guid deliveryId, ClaimsPrincipal driver, DeliveryDbContext db, CancellationToken ct)
    {
        var driverId = driver.UserId();
        var restaurantId = driver.RestaurantId();

        var rows = await db.Deliveries
            .Where(d => d.Id == deliveryId && d.RestaurantId == restaurantId && d.Status == DeliveryStatus.Requested)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.DriverId, driverId)
                .SetProperty(d => d.Status, DeliveryStatus.Accepted)
                .SetProperty(d => d.AcceptedAt, DateTimeOffset.UtcNow), ct);

        return rows == 1 ? Results.NoContent() : Problems.Conflict("This delivery was already taken by another driver.");
    }
}
