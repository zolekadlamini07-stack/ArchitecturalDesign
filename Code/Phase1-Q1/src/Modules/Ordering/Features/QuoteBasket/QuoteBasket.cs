using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.Domain;
using FoodDelivery.Modules.Restaurants.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDelivery.Modules.Ordering.Features.QuoteBasket;

/// <summary>
/// SLICE: "What will my basket cost?" (brief: Customers - "add items to a basket").
///
/// The basket itself lives on the CLIENT (phone/browser) - we don't store it. This slice just
/// recalculates the totals on the server using real prices, with the SAME OrderPricing formula
/// PlaceOrder uses, so the preview and the real charge can never disagree.
/// </summary>
internal static class QuoteBasket
{
    public sealed record Line(Guid MenuItemId, int Quantity);
    public sealed record Request(Guid RestaurantId, IReadOnlyList<Line> Items);
    public sealed record Response(decimal ItemsTotal, decimal DeliveryFee, decimal PlatformFee, decimal Total, string Currency);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders/quote", Handle).AllowAnonymous().WithTags("Ordering");

    private static async Task<IResult> Handle(Request request, IRestaurantsApi restaurants, CancellationToken ct)
    {
        var restaurant = await restaurants.GetRestaurantAsync(request.RestaurantId, ct);
        if (restaurant is null) return Problems.NotFound("Restaurant");

        var priced = (await restaurants.GetPricedItemsAsync(request.RestaurantId, request.Items.Select(i => i.MenuItemId).ToList(), ct))
            .ToDictionary(p => p.ItemId);
        if (request.Items.Any(i => !priced.ContainsKey(i.MenuItemId)))
            return Problems.Validation(nameof(request.Items), "Unknown menu item.");

        var breakdown = OrderPricing.Calculate(request.Items.Select(i => (priced[i.MenuItemId].Price, i.Quantity)).ToList(), restaurant.DeliveryFee);
        return Results.Ok(new Response(breakdown.ItemsTotal.Amount, breakdown.DeliveryFee.Amount, breakdown.PlatformFee.Amount,
            breakdown.Total.Amount, breakdown.Total.Currency));
    }
}
