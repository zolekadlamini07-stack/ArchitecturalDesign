using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Restaurants.Caching;
using FoodDelivery.Modules.Restaurants.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Restaurants.Features.ManageRestaurant;

/// <summary>
/// SLICE: "Platform admin onboards a restaurant". Q1: the platform did this by hand. Q2: still available to admins,
/// alongside the new self-service flow (Features/Onboarding).
/// </summary>
internal static class CreateRestaurant
{
    public sealed record Request(string Name, string Address, decimal DeliveryFee, string Currency = "ZAR");
    public sealed record Response(Guid RestaurantId);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/restaurants", Handle).RequireAuthorization(p => p.RequireRole(Roles.Admin)).WithTags("Restaurants");

    private static async Task<IResult> Handle(Request request, RestaurantsDbContext db, RestaurantCache cache, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return Problems.Validation(nameof(request.Name), "Name is required.");
        if (request.DeliveryFee < 0) return Problems.Validation(nameof(request.DeliveryFee), "Delivery fee cannot be negative.");

        var restaurant = new Restaurant
        {
            Id = Guid.NewGuid(), Name = request.Name.Trim(), Address = request.Address.Trim(),
            DeliveryFee = request.DeliveryFee, Currency = request.Currency, IsOpen = false, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateListAsync(ct);
        return Results.Created($"/restaurants/{restaurant.Id}", new Response(restaurant.Id));
    }
}

/// <summary>
/// SLICE: "Restaurant opens or closes". Staff can only change THEIR restaurant: the id comes from
/// the token, not the URL, so one restaurant can never edit another.
/// </summary>
internal static class SetOpenStatus
{
    public sealed record Request(bool IsOpen);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/restaurants/mine/open-status", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Restaurants");

    private static async Task<IResult> Handle(Request request, ClaimsPrincipal staff, RestaurantsDbContext db, RestaurantCache cache, CancellationToken ct)
    {
        var updated = await db.Restaurants.Where(r => r.Id == staff.RestaurantId())
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsOpen, request.IsOpen), ct);
        await cache.InvalidateListAsync(ct);                        // open/closed shows in the browse list...
        await cache.InvalidateMenuAsync(staff.RestaurantId(), ct);  // ...and on the menu page
        return updated == 1 ? Results.NoContent() : Problems.NotFound("Restaurant");
    }
}
