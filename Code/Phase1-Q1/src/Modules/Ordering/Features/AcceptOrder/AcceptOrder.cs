using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Notifications.PublicApi;
using FoodDelivery.Modules.Ordering.Data;
using FoodDelivery.Modules.Ordering.Domain;
using FoodDelivery.Modules.Payments.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Ordering.Features.AcceptOrder;

/// <summary>
/// SLICE: "Restaurant accepts an order" (brief: Restaurants - "accept or reject orders").
/// Accepting = CAPTURE the money that was held at checkout (D6).
/// </summary>
internal static class AcceptOrder
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders/{orderId:guid}/accept", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Ordering");

    private static async Task<IResult> Handle(Guid orderId, ClaimsPrincipal staff, OrderingDbContext db,
        IPaymentsApi payments, INotificationsApi notifications, CancellationToken ct)
    {
        // A restaurant can only touch ITS OWN orders (restaurant id from the token).
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.RestaurantId == staff.RestaurantId(), ct);
        if (order is null) return Problems.NotFound("Order");

        try { order.Accept(); }                       // the state machine decides if this is allowed
        catch (OrderRuleException ex) { return Problems.Conflict(ex.Message); }

        var capture = await payments.CaptureAsync(order.Id, ct);
        if (!capture.IsSuccess) return Problems.Conflict($"Could not take payment: {capture.Reason}");

        await db.SaveChangesAsync(ct);
        await notifications.NotifyAsync(NotificationRecipient.Customer(order.CustomerId), "Order accepted", "The restaurant accepted your order.", ct);
        return Results.NoContent();
    }
}

/// <summary>
/// SLICE: "Restaurant rejects an order". Rejecting = VOID the hold. The customer is never charged,
/// so no refund is ever needed for a rejection (the main reason for authorise-then-capture).
/// </summary>
internal static class RejectOrder
{
    public sealed record Request(string Reason);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders/{orderId:guid}/reject", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Ordering");

    private static async Task<IResult> Handle(Guid orderId, Request request, ClaimsPrincipal staff, OrderingDbContext db,
        IPaymentsApi payments, INotificationsApi notifications, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.RestaurantId == staff.RestaurantId(), ct);
        if (order is null) return Problems.NotFound("Order");

        try { order.Reject(request.Reason); }
        catch (OrderRuleException ex) { return Problems.Conflict(ex.Message); }

        await payments.VoidAsync(order.Id, ct);
        await db.SaveChangesAsync(ct);
        await notifications.NotifyAsync(NotificationRecipient.Customer(order.CustomerId), "Order not accepted",
            $"The restaurant could not take your order ({request.Reason}). You have not been charged.", ct);
        return Results.NoContent();
    }
}
