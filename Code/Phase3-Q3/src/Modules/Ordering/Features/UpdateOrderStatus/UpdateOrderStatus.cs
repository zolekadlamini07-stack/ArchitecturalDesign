using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Delivery.PublicApi;
using FoodDelivery.Modules.Ordering.Data;
using FoodDelivery.Modules.Ordering.Domain;
using FoodDelivery.Modules.Ordering.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Ordering.Features.UpdateOrderStatus;

// Brief: Restaurants - "update the order status".

/// <summary>SLICE: "We've started cooking".</summary>
internal static class MarkPreparing
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders/{orderId:guid}/preparing", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Ordering");

    private static async Task<IResult> Handle(Guid orderId, ClaimsPrincipal staff, OrderingDbContext db, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.RestaurantId == staff.RestaurantId(), ct);
        if (order is null) return Problems.NotFound("Order");
        try { order.StartPreparing(); } catch (OrderRuleException ex) { return Problems.Conflict(ex.Message); }
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

/// <summary>
/// SLICE: "Food is ready - get a driver".
/// This is the COMMAND direction: Ordering TELLS Delivery to create a request (public API call).
/// The way back (picked up / delivered) comes as EVENTS, see the handlers below.
/// </summary>
internal static class MarkReadyForPickup
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders/{orderId:guid}/ready", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Ordering");

    private static async Task<IResult> Handle(Guid orderId, ClaimsPrincipal staff, OrderingDbContext db, IDeliveryApi delivery, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.RestaurantId == staff.RestaurantId(), ct);
        if (order is null) return Problems.NotFound("Order");
        try { order.MarkReadyForPickup(); } catch (OrderRuleException ex) { return Problems.Conflict(ex.Message); }

        await db.SaveChangesAsync(ct);
        await delivery.RequestDeliveryAsync(order.Id, order.RestaurantId, order.DeliveryAddress, ct);
        return Results.NoContent();
    }
}

// ---------------------------------------------------------------------------------------------
// EVENT HANDLER SLICES - Ordering reacting to FACTS published by the Delivery module.
// Delivery never calls Ordering; it announces, and these handlers move the order along.
// Q2 CHANGE: they now run in the WORKER as jobs (from Delivery's outbox), with retries.
// They may run twice, so each one checks the current status first (idempotent).
// ---------------------------------------------------------------------------------------------

/// <summary>Driver collected the food -> order is OUT_FOR_DELIVERY.</summary>
internal sealed class WhenDeliveryPickedUp(OrderingDbContext db) : IDomainEventHandler<DeliveryPickedUp>
{
    public async Task HandleAsync(DeliveryPickedUp e, CancellationToken ct)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == e.OrderId, ct);
        if (order.Status == OrderStatus.OutForDelivery) return; // already applied: safe to repeat
        order.MarkOutForDelivery();
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Food handed over -> order is DELIVERED, and OrderDelivered is published.</summary>
internal sealed class WhenDeliveryCompleted(OrderingDbContext db) : IDomainEventHandler<DeliveryCompleted>
{
    public async Task HandleAsync(DeliveryCompleted e, CancellationToken ct)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == e.OrderId, ct);
        if (order.Status == OrderStatus.Delivered) return;
        order.MarkDelivered();
        // Q2: "tell the customer" is now an event too - Notifications picks it up.
        db.AddToOutbox(new OrderDelivered(order.Id, order.CustomerId, order.RestaurantId));
        await db.SaveChangesAsync(ct);
    }
}
