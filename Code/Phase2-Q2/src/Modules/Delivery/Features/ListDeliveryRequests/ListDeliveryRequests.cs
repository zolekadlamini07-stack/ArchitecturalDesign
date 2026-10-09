using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Delivery.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Delivery.Features.ListDeliveryRequests;

/// <summary>
/// SLICE: "Show me delivery requests" (brief: Drivers - "receive delivery requests").
///
/// D7 IN ACTION: a driver only sees requests from THEIR OWN restaurant, and only while on shift.
/// The restaurant id comes from the driver's token, so a driver can never see another
/// restaurant's deliveries.
/// </summary>
internal static class ListDeliveryRequests
{
    public sealed record Item(Guid DeliveryId, Guid OrderId, string DeliveryAddress, DateTimeOffset RequestedAt);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/delivery/requests", Handle).RequireAuthorization(p => p.RequireRole(Roles.Driver)).WithTags("Delivery");

    private static async Task<IResult> Handle(ClaimsPrincipal driver, DeliveryDbContext db, CancellationToken ct)
    {
        var driverId = driver.UserId();
        var onShift = await db.Shifts.AnyAsync(s => s.DriverId == driverId && s.EndedAt == null, ct);
        if (!onShift) return Results.Ok(Array.Empty<Item>()); // off shift = no requests

        var restaurantId = driver.RestaurantId();
        var items = await db.Deliveries.AsNoTracking()
            .Where(d => d.RestaurantId == restaurantId && d.Status == DeliveryStatus.Requested)
            .OrderBy(d => d.RequestedAt)
            .Select(d => new Item(d.Id, d.OrderId, d.DeliveryAddress, d.RequestedAt))
            .ToListAsync(ct);
        return Results.Ok(items);
    }
}
