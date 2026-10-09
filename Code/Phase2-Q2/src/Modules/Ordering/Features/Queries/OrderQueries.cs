using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.Data;
using FoodDelivery.Modules.Ordering.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Ordering.Features.Queries;

// ============================================================================================
// READ SLICES. Compare these with PlaceOrder: no domain logic, no other modules, no rules -
// just "query -> shape the answer". In layered architecture they would still be forced through a
// controller, a service, a repository and a mapper. In vertical slices each slice is only as
// complex as its job. (That is the main argument for D2.)
// ============================================================================================

/// <summary>SLICE: "Where is my order?" (brief: Customers - "track the order"). The app polls this (D8).</summary>
internal static class TrackOrder
{
    public sealed record Response(Guid OrderId, string Status, decimal Total, string Currency, DateTimeOffset CreatedAt);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders/{orderId:guid}", Handle).RequireAuthorization(p => p.RequireRole(Roles.Customer)).WithTags("Ordering");

    private static async Task<IResult> Handle(Guid orderId, ClaimsPrincipal customer, OrderingDbContext db, CancellationToken ct)
    {
        var customerId = customer.UserId();
        var order = await db.Orders.AsNoTracking().IgnoreAutoIncludes()
            .Where(o => o.Id == orderId && o.CustomerId == customerId)
            .Select(o => new Response(o.Id, o.Status.ToString(), o.Total, o.Currency, o.CreatedAt))
            .SingleOrDefaultAsync(ct);
        return order is null ? Problems.NotFound("Order") : Results.Ok(order);
    }
}

/// <summary>SLICE: "My previous orders" (brief: Customers - "view previous orders"). Paginated.</summary>
internal static class ListMyOrders
{
    public sealed record Item(Guid OrderId, Guid RestaurantId, string Status, decimal Total, string Currency, DateTimeOffset CreatedAt);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders/mine", Handle).RequireAuthorization(p => p.RequireRole(Roles.Customer)).WithTags("Ordering");

    private static async Task<IResult> Handle(ClaimsPrincipal customer, OrderingDbContext db, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var customerId = customer.UserId();
        var items = await db.Orders.AsNoTracking().IgnoreAutoIncludes()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new Item(o.Id, o.RestaurantId, o.Status.ToString(), o.Total, o.Currency, o.CreatedAt))
            .ToListAsync(ct);
        return Results.Ok(new Page<Item>(items, page, pageSize));
    }
}

/// <summary>
/// SLICE: "Orders waiting for me" (brief: Restaurants - "receive orders").
/// The dashboard POLLS this. It is the source of truth: if a push notification is lost, the order
/// still appears here, so no order is ever missed.
/// </summary>
internal static class ListIncomingOrders
{
    public sealed record Item(Guid OrderId, string Status, string DeliveryAddress, decimal Total, DateTimeOffset CreatedAt,
        IReadOnlyList<string> Lines);

    private static readonly OrderStatus[] Active =
        [OrderStatus.AwaitingAcceptance, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.ReadyForPickup];

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders/incoming", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Ordering");

    private static async Task<IResult> Handle(ClaimsPrincipal staff, OrderingDbContext db, CancellationToken ct)
    {
        var restaurantId = staff.RestaurantId();
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId && Active.Contains(o.Status))
            .OrderBy(o => o.CreatedAt) // first in, first out
            .ToListAsync(ct);
        return Results.Ok(orders.Select(o => new Item(o.Id, o.Status.ToString(), o.DeliveryAddress, o.Total, o.CreatedAt,
            o.Lines.Select(l => $"{l.Quantity} x {l.ItemName}").ToList())));
    }
}

/// <summary>SLICE: "Our previous orders" (brief: Restaurants - "see previous orders"). Paginated.</summary>
internal static class ListRestaurantOrderHistory
{
    public sealed record Item(Guid OrderId, string Status, decimal Total, DateTimeOffset CreatedAt);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders/history", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Ordering");

    private static async Task<IResult> Handle(ClaimsPrincipal staff, OrderingDbContext db, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var restaurantId = staff.RestaurantId();
        var items = await db.Orders.AsNoTracking().IgnoreAutoIncludes()
            .Where(o => o.RestaurantId == restaurantId)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new Item(o.Id, o.Status.ToString(), o.Total, o.CreatedAt))
            .ToListAsync(ct);
        return Results.Ok(new Page<Item>(items, page, pageSize));
    }
}
