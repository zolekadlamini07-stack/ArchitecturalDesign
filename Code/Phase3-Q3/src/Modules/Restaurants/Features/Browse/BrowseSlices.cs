using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Restaurants.Caching;
using FoodDelivery.Modules.Restaurants.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Restaurants.Features.Browse;

/// <summary>
/// SLICE: "Browse restaurants".
/// Q2 CHANGES (compare with Phase 1):
///   - CACHED in Redis (problem P1: the busiest endpoint in the system)
///   - filtering + pagination (50 restaurants coming; still no search engine - one indexed query is enough)
///   - only APPROVED restaurants are listed (self-service applications wait for approval)
/// </summary>
internal static class BrowseRestaurants
{
    public sealed record Item(Guid RestaurantId, string Name, string Address, string City, bool IsOpen, decimal DeliveryFee, string Currency);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/restaurants", Handle).AllowAnonymous().WithTags("Restaurants");

    private static async Task<IResult> Handle(RestaurantsDbContext db, RestaurantCache cache, CancellationToken ct,
        string? search = null, string? city = null, bool openOnly = false, int page = 1, int pageSize = 20)
    {
        var key = await cache.ListKeyAsync($"{city?.ToLowerInvariant()}:{search?.ToLowerInvariant()}:{openOnly}:{page}:{pageSize}", ct);

        var items = await cache.GetOrCreateAsync(key, async () =>
        {
            var query = db.Restaurants.AsNoTracking().Where(r => r.ApprovalStatus == ApprovalStatus.Active);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(r => EF.Functions.ILike(r.Name, $"%{search}%"));
            if (!string.IsNullOrWhiteSpace(city)) query = query.Where(r => r.City == city); // Q3: browse by city
            if (openOnly) query = query.Where(r => r.IsOpen);
            return await query.OrderBy(r => r.Name)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => new Item(r.Id, r.Name, r.Address, r.City, r.IsOpen, r.DeliveryFee, r.Currency))
                .ToListAsync(ct);
        }, ct);

        return Results.Ok(new Page<Item>(items, page, pageSize));
    }
}

/// <summary>
/// SLICE: "View a menu". Q2: cache-aside on key menu:{restaurantId}, 5-minute TTL.
/// Most views never touch PostgreSQL now.
/// </summary>
internal static class GetMenu
{
    public sealed record MenuLine(Guid ItemId, string Name, string? Description, decimal Price, bool IsAvailable);
    public sealed record Response(Guid RestaurantId, string RestaurantName, bool IsOpen, string Currency, IReadOnlyList<MenuLine> Items);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/restaurants/{restaurantId:guid}/menu", Handle).AllowAnonymous().WithTags("Restaurants");

    private static async Task<IResult> Handle(Guid restaurantId, RestaurantsDbContext db, RestaurantCache cache, CancellationToken ct)
    {
        var menu = await cache.GetOrCreateAsync<Response?>(RestaurantCache.MenuKey(restaurantId), async () =>
        {
            var restaurant = await db.Restaurants.AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == restaurantId && r.ApprovalStatus == ApprovalStatus.Active, ct);
            if (restaurant is null) return null;

            var items = await db.MenuItems.AsNoTracking()
                .Where(i => i.RestaurantId == restaurantId)
                .OrderBy(i => i.Name)
                .Select(i => new MenuLine(i.Id, i.Name, i.Description, i.Price, i.IsAvailable))
                .ToListAsync(ct);
            return new Response(restaurant.Id, restaurant.Name, restaurant.IsOpen, restaurant.Currency, items);
        }, ct);

        return menu is null ? Results.NotFound() : Results.Ok(menu);
    }
}
