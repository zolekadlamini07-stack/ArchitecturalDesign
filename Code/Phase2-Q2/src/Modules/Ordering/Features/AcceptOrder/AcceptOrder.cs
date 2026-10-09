using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.Data;
using FoodDelivery.Modules.Ordering.Domain;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.Payments.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Ordering.Features.AcceptOrder;

/// <summary>
/// SLICE: "Restaurant accepts an order". Accepting = CAPTURE the money held at checkout (D6).
/// Q2 CHANGE: no notification call here any more - OrderAccepted goes into the outbox with the order.
/// </summary>
internal static class AcceptOrder
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders/{orderId:guid}/accept", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Ordering");

    private static async Task<IResult> Handle(Guid orderId, ClaimsPrincipal staff, OrderingDbContext db, IPaymentsApi payments, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.RestaurantId == staff.RestaurantId(), ct);
        if (order is null) return Problems.NotFound("Order");

        try { order.Accept(); }
        catch (OrderRuleException ex) { return Problems.Conflict(ex.Message); }

        var capture = await payments.CaptureAsync(order.Id, ct);
        if (!capture.IsSuccess) return Problems.Conflict($"Could not take payment: {capture.Reason}");

        db.AddToOutbox(new OrderAccepted(order.Id, order.CustomerId, order.RestaurantId));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

/// <summary>SLICE: "Restaurant rejects an order". Rejecting = VOID the hold; the customer is never charged.</summary>
internal static class RejectOrder
{
    public sealed record Request(string Reason);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders/{orderId:guid}/reject", Handle).RequireAuthorization(p => p.RequireRole(Roles.RestaurantStaff)).WithTags("Ordering");

    private static async Task<IResult> Handle(Guid orderId, Request request, ClaimsPrincipal staff, OrderingDbContext db, IPaymentsApi payments, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.RestaurantId == staff.RestaurantId(), ct);
        if (order is null) return Problems.NotFound("Order");

        try { order.Reject(request.Reason); }
        catch (OrderRuleException ex) { return Problems.Conflict(ex.Message); }

        await payments.VoidAsync(order.Id, ct);
        db.AddToOutbox(new OrderRejected(order.Id, order.CustomerId, order.RestaurantId, request.Reason));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
