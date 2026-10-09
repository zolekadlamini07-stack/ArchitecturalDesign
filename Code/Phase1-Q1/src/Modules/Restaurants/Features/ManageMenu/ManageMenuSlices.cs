using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Restaurants.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Restaurants.Features.ManageMenu;

// Brief: Restaurants - "Manage their menu". Three small slices that belong together, so they share
// one file. Each is still its own self-contained use case (endpoint + request + handler).
//
// Q2 NOTE: once menus are cached, each of these slices also deletes the cached menu after saving
// ("cache invalidation"). In Q1 there is no cache, so they just save.

/// <summary>SLICE: "Add a menu item".</summary>
internal static class AddMenuItem
{
    public sealed record Request(string Name, string? Description, decimal Price);
    public sealed record Response(Guid ItemId);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/restaurants/mine/menu-items", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Menu");

    private static async Task<IResult> Handle(Request request, ClaimsPrincipal staff, RestaurantsDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return Problems.Validation(nameof(request.Name), "Name is required.");
        if (request.Price <= 0) return Problems.Validation(nameof(request.Price), "Price must be greater than zero.");

        var item = new MenuItem
        {
            Id = Guid.NewGuid(), RestaurantId = staff.RestaurantId(), Name = request.Name.Trim(),
            Description = request.Description, Price = request.Price, IsAvailable = true,
        };
        db.MenuItems.Add(item);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/restaurants/mine/menu-items/{item.Id}", new Response(item.Id));
    }
}

/// <summary>SLICE: "Edit a menu item (name, description, price)".</summary>
internal static class UpdateMenuItem
{
    public sealed record Request(string Name, string? Description, decimal Price);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/restaurants/mine/menu-items/{itemId:guid}", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Menu");

    private static async Task<IResult> Handle(Guid itemId, Request request, ClaimsPrincipal staff, RestaurantsDbContext db, CancellationToken ct)
    {
        if (request.Price <= 0) return Problems.Validation(nameof(request.Price), "Price must be greater than zero.");
        var item = await db.MenuItems.SingleOrDefaultAsync(i => i.Id == itemId && i.RestaurantId == staff.RestaurantId(), ct);
        if (item is null) return Problems.NotFound("Menu item");

        item.Name = request.Name.Trim();
        item.Description = request.Description;
        item.Price = request.Price; // existing orders are unaffected: they keep a price SNAPSHOT
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

/// <summary>SLICE: "Mark an item available / sold out".</summary>
internal static class SetItemAvailability
{
    public sealed record Request(bool IsAvailable);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/restaurants/mine/menu-items/{itemId:guid}/availability", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Menu");

    private static async Task<IResult> Handle(Guid itemId, Request request, ClaimsPrincipal staff, RestaurantsDbContext db, CancellationToken ct)
    {
        var updated = await db.MenuItems.Where(i => i.Id == itemId && i.RestaurantId == staff.RestaurantId())
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsAvailable, request.IsAvailable), ct);
        return updated == 1 ? Results.NoContent() : Problems.NotFound("Menu item");
    }
}
