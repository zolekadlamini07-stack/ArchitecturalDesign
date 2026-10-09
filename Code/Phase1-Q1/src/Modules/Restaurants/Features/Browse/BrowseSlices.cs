using FoodDelivery.Modules.Restaurants.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Restaurants.Features.Browse;

/// <summary>
/// SLICE: "Browse restaurants" (brief: Customers - "browse restaurants").
/// Q1: a straight database query on every request. With one restaurant that is perfectly fine.
/// Q2 NOTE: this becomes the busiest endpoint in the system (problem P1) and gets a Redis cache.
/// </summary>
internal static class BrowseRestaurants
{
    public sealed record Item(Guid RestaurantId, string Name, string Address, bool IsOpen, decimal DeliveryFee, string Currency);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/restaurants", Handle).AllowAnonymous().WithTags("Restaurants");

    private static async Task<IResult> Handle(RestaurantsDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Restaurants.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new Item(r.Id, r.Name, r.Address, r.IsOpen, r.DeliveryFee, r.Currency))
            .ToListAsync(ct));
}

/// <summary>
/// SLICE: "View a menu" (brief: Customers - "view menus").
/// Q1: straight database query. Q2: cache-aside in Redis.
/// </summary>
internal static class GetMenu
{
    public sealed record MenuLine(Guid ItemId, string Name, string? Description, decimal Price, bool IsAvailable);
    public sealed record Response(Guid RestaurantId, string RestaurantName, bool IsOpen, string Currency, IReadOnlyList<MenuLine> Items);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/restaurants/{restaurantId:guid}/menu", Handle).AllowAnonymous().WithTags("Restaurants");

    private static async Task<IResult> Handle(Guid restaurantId, RestaurantsDbContext db, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.AsNoTracking().SingleOrDefaultAsync(r => r.Id == restaurantId, ct);
        if (restaurant is null) return Results.NotFound();

        var items = await db.MenuItems.AsNoTracking()
            .Where(i => i.RestaurantId == restaurantId)
            .OrderBy(i => i.Name)
            .Select(i => new MenuLine(i.Id, i.Name, i.Description, i.Price, i.IsAvailable))
            .ToListAsync(ct);

        return Results.Ok(new Response(restaurant.Id, restaurant.Name, restaurant.IsOpen, restaurant.Currency, items));
    }
}
