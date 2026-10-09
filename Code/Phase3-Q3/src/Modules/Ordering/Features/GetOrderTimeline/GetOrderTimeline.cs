using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.Data;
using FoodDelivery.Modules.Payments.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Ordering.Features.GetOrderTimeline;

/// <summary>
/// Q3 NEW SLICE: "What REALLY happened to this order and its money?" (support view, change C8).
///
/// On Friday night customer support was flooded and had no single place to look. This merges:
///   - the order's status history (Ordering's own table)
///   - the payment log (from Payments' PUBLIC API - Ordering still never reads payments tables)
/// into one timeline, oldest first. Support can now say "your card was held at 19:02, the reply
/// was lost, we confirmed it at 19:04 - you were charged once" in seconds.
/// </summary>
internal static class GetOrderTimeline
{
    public sealed record Entry(DateTimeOffset At, string Source, string What, string? Detail);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/support/orders/{orderId:guid}/timeline", Handle).RequireAuthorization(p => p.RequireRole(Roles.Admin)).WithTags("Support");

    private static async Task<IResult> Handle(Guid orderId, OrderingDbContext db, IPaymentsApi payments, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.History).SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return Problems.NotFound("Order");

        var entries = order.History
            .Select(h => new Entry(h.ChangedAt, "order", $"{h.FromStatus?.ToString() ?? "(new)"} -> {h.ToStatus}", $"by {h.ChangedBy}"))
            .Concat((await payments.GetTimelineAsync(orderId, ct))
                .Select(p => new Entry(p.At, "payment", p.What, p.Detail)))
            .OrderBy(e => e.At)
            .ToList();

        return Results.Ok(new { orderId, status = order.Status.ToString(), order.Total, order.Currency, timeline = entries });
    }
}
