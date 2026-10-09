using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Customers.PublicApi;
using FoodDelivery.Modules.Notifications.PublicApi;
using FoodDelivery.Modules.Ordering.Data;
using FoodDelivery.Modules.Ordering.Domain;
using FoodDelivery.Modules.Payments.PublicApi;
using FoodDelivery.Modules.Restaurants.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDelivery.Modules.Ordering.Features.PlaceOrder;

// ============================================================================================
// VERTICAL SLICE: "Place an order and pay for it"
// (brief: Customers - "place an order", "pay for the order")
//
// The Q1 walkthrough from Question1.md section 4, step by step:
//   1. The browser already turned the card into a token (provider's hosted fields).
//   2. POST /orders with basket + address + payment token.
//   3. Ask Restaurants (PUBLIC API) for real prices - the client's prices are ignored.
//   4. Save the order as PLACED.
//   5. Ask Payments (PUBLIC API) to AUTHORISE (hold) the total.
//        success -> AWAITING_ACCEPTANCE, notify the restaurant
//        failure -> PAYMENT_FAILED, customer may try again
//
// Notice the slice talks to FOUR other modules, and only ever through their PublicApi interfaces.
// It cannot see their tables or classes - the compiler won't allow it.
// ============================================================================================
internal static class PlaceOrder
{
    public sealed record Line(Guid MenuItemId, int Quantity);
    public sealed record Request(Guid RestaurantId, Guid AddressId, IReadOnlyList<Line> Items, string PaymentToken);
    public sealed record Response(Guid OrderId, string Status, decimal Total, string Currency);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", Handle).RequireAuthorization(p => p.RequireRole(Roles.Customer)).WithTags("Ordering");

    private static async Task<IResult> Handle(
        Request request,
        ClaimsPrincipal customer,
        OrderingDbContext db,
        IRestaurantsApi restaurants,        // public API of Restaurants
        ICustomersApi customers,            // public API of Customers
        IPaymentsApi payments,              // public API of Payments
        INotificationsApi notifications,    // public API of Notifications
        CancellationToken ct)
    {
        var customerId = customer.UserId();
        if (request.Items is not { Count: > 0 }) return Problems.Validation(nameof(request.Items), "Add at least one item.");
        if (string.IsNullOrWhiteSpace(request.PaymentToken)) return Problems.Validation(nameof(request.PaymentToken), "Payment details are required.");

        // --- 3. Server-side facts from other modules (never trust the client) ---------------
        var restaurant = await restaurants.GetRestaurantAsync(request.RestaurantId, ct);
        if (restaurant is null) return Problems.NotFound("Restaurant");
        if (!restaurant.IsOpen) return Problems.Conflict("This restaurant is closed.");

        var address = await customers.GetDeliveryAddressAsync(customerId, request.AddressId, ct);
        if (address is null) return Problems.Validation(nameof(request.AddressId), "Unknown delivery address.");

        var itemIds = request.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var priced = (await restaurants.GetPricedItemsAsync(request.RestaurantId, itemIds, ct)).ToDictionary(p => p.ItemId);
        var missing = itemIds.Where(id => !priced.TryGetValue(id, out var p) || !p.IsAvailable).ToList();
        if (missing.Count > 0) return Problems.Conflict($"Some items are unavailable: {string.Join(", ", missing)}");

        // --- 4. Create and save the order (the Order aggregate enforces the rules) ----------
        Order order;
        try
        {
            order = Order.Place(customerId, request.RestaurantId, address.ToString(),
                request.Items.Select(i => (i.MenuItemId, priced[i.MenuItemId].Name, priced[i.MenuItemId].Price, i.Quantity)).ToList(),
                restaurant.DeliveryFee);
        }
        catch (OrderRuleException ex)
        {
            return Problems.Validation(nameof(request.Items), ex.Message);
        }
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        // --- 5. Authorise payment - SYNCHRONOUSLY, inside this HTTP request (Q1) -------------
        // The customer waits while we call the provider. Fine at Q1 scale; a slow provider makes
        // this request slow (Risk R2/R3). Q3 moves this call to a background worker.
        var payment = await payments.AuthoriseAsync(order.Id, order.TotalAsMoney, request.PaymentToken, ct);

        if (!payment.IsSuccess)
        {
            order.MarkPaymentFailed();
            await db.SaveChangesAsync(ct);
            return Results.Problem(statusCode: StatusCodes.Status402PaymentRequired,
                title: "Payment failed, please try again.", detail: payment.Reason);
        }

        order.MarkPaymentAuthorised();
        await db.SaveChangesAsync(ct);

        // Q1: notify synchronously - this request waits ~300 ms more for the push provider.
        // Q2 PROBLEM P3 ("notifications are slowing down certain operations") starts right here.
        await notifications.NotifyAsync(NotificationRecipient.Restaurant(order.RestaurantId),
            "New order", $"Order {order.Id} is waiting for you to accept it.", ct);

        return Results.Created($"/orders/{order.Id}", new Response(order.Id, order.Status.ToString(), order.Total, order.Currency));
    }
}
