using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Customers.PublicApi;
using FoodDelivery.Modules.Ordering.Data;
using FoodDelivery.Modules.Ordering.Domain;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.Restaurants.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodDelivery.Modules.Ordering.Features.PlaceOrder;

// ============================================================================================
// VERTICAL SLICE: "Place an order" - Q3 VERSION (compare with Phase 1 and Phase 2).
//
// Q1/Q2: validate -> save -> CALL THE PAYMENT PROVIDER (customer waits) -> timeout = "failed"
// Q3:    validate -> save ORDER (PAYMENT_PENDING) + OrderPlaced in ONE transaction -> 202 Accepted
//        The Payments module authorises in the Worker. The customer's screen shows
//        "Confirming your payment..." and polls GET /orders/{id}.
//
// WHY ORDER FIRST? (Q3 change C1, D18)
//   Payment first, order second: payment succeeds, order creation crashes -> CHARGED, NO ORDER.
//   Order first, payment second: the worst case is an order with no payment - visible, and it
//   expires after 10 minutes. A payment can only ever exist FOR AN ORDER WE HAVE.
//
// DUPLICATE PROTECTION, layers 1 and 2 (Q3 walkthrough 5.3):
//   1. Idempotency-Key header: same key -> the SAME order comes back (double-click, network retry)
//   2. One PAYMENT_PENDING order per customer (database index) -> page refresh / new key
// ============================================================================================
internal static class PlaceOrder
{
    public sealed record Line(Guid MenuItemId, int Quantity);
    public sealed record Request(Guid RestaurantId, Guid AddressId, IReadOnlyList<Line> Items, string PaymentToken);
    public sealed record Response(Guid OrderId, string Status, decimal Total, string Currency, string Message);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", Handle).RequireAuthorization(p => p.RequireRole(Roles.Customer)).WithTags("Ordering");

    private static async Task<IResult> Handle(
        Request request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ClaimsPrincipal customer,
        OrderingDbContext db,
        IRestaurantsApi restaurants,
        ICustomersApi customers,
        CancellationToken ct)
    {
        var customerId = customer.UserId();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Problems.Validation("Idempotency-Key", "Send one Idempotency-Key header per checkout (e.g. a new GUID).");

        // --- Layer 1: have we seen this exact checkout before? Return the SAME order. ----------
        if (await FindByKeyAsync(db, customerId, idempotencyKey, ct) is { } sameOrder)
            return Accepted(sameOrder, "We already have this order.");

        // --- Layer 2: is another order of theirs still confirming payment? Show that one. -------
        if (await FindPendingAsync(db, customerId, ct) is { } pending)
            return PendingConflict(pending);

        // --- Validate with server-side facts (unchanged since Q1) ------------------------------
        if (request.Items is not { Count: > 0 }) return Problems.Validation(nameof(request.Items), "Add at least one item.");
        if (string.IsNullOrWhiteSpace(request.PaymentToken)) return Problems.Validation(nameof(request.PaymentToken), "Payment details are required.");

        var restaurant = await restaurants.GetRestaurantAsync(request.RestaurantId, ct);
        if (restaurant is null) return Problems.NotFound("Restaurant");
        if (!restaurant.IsOpen) return Problems.Conflict("This restaurant is closed.");
        var address = await customers.GetDeliveryAddressAsync(customerId, request.AddressId, ct);
        if (address is null) return Problems.Validation(nameof(request.AddressId), "Unknown delivery address.");
        var itemIds = request.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var priced = (await restaurants.GetPricedItemsAsync(request.RestaurantId, itemIds, ct)).ToDictionary(p => p.ItemId);
        if (itemIds.Any(id => !priced.TryGetValue(id, out var p) || !p.IsAvailable)) return Problems.Conflict("Some items are unavailable.");

        Order order;
        try
        {
            order = Order.Place(customerId, request.RestaurantId, address.ToString(),
                request.Items.Select(i => (i.MenuItemId, priced[i.MenuItemId].Name, priced[i.MenuItemId].Price, i.Quantity)).ToList(),
                restaurant.DeliveryFee, restaurant.IsPartner ? Fulfilment.Partner : Fulfilment.Own);
        }
        catch (OrderRuleException ex) { return Problems.Validation(nameof(request.Items), ex.Message); }

        // --- ONE TRANSACTION: order + idempotency key + OrderPlaced event ----------------------
        db.Orders.Add(order);
        db.PlaceOrderRequests.Add(new PlaceOrderRequest { CustomerId = customerId, IdempotencyKey = idempotencyKey, OrderId = order.Id, CreatedAt = DateTimeOffset.UtcNow });
        db.AddToOutbox(new OrderPlaced(order.Id, customerId, order.RestaurantId, order.Total, order.Currency, request.PaymentToken));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two requests raced (e.g. a double-click hitting two API instances at once).
            // The database let exactly one win; show the winner to the loser.
            db.ChangeTracker.Clear();
            if (await FindByKeyAsync(db, customerId, idempotencyKey, ct) is { } winner) return Accepted(winner, "We already have this order.");
            if (await FindPendingAsync(db, customerId, ct) is { } other) return PendingConflict(other);
            throw;
        }

        return Accepted(order, "Confirming your payment...");
    }

    private static Task<Order?> FindByKeyAsync(OrderingDbContext db, Guid customerId, string key, CancellationToken ct) =>
        db.PlaceOrderRequests.Where(r => r.CustomerId == customerId && r.IdempotencyKey == key)
            .Join(db.Orders, r => r.OrderId, o => o.Id, (r, o) => o).FirstOrDefaultAsync(ct);

    private static Task<Order?> FindPendingAsync(OrderingDbContext db, Guid customerId, CancellationToken ct) =>
        db.Orders.FirstOrDefaultAsync(o => o.CustomerId == customerId && o.Status == OrderStatus.PaymentPending, ct);

    private static IResult Accepted(Order order, string message) =>
        Results.Accepted($"/orders/{order.Id}", new Response(order.Id, order.Status.ToString(), order.Total, order.Currency, message));

    private static IResult PendingConflict(Order pending) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "You already have an order being confirmed. Please don't place it again - we'll update you here.",
            extensions: new Dictionary<string, object?> { ["orderId"] = pending.Id });
}
