using FoodDelivery.BuildingBlocks;

namespace FoodDelivery.Modules.Ordering.Domain;

/// <summary>
/// The order total formula (from 02-SystemFlowchart.md), in one place:
///   items_total  = SUM(price x quantity)
///   subtotal     = items_total + delivery_fee
///   platform_fee = subtotal x 10%
///   total        = subtotal + platform_fee
/// Used by both QuoteBasket (preview) and PlaceOrder (the real thing), so they can never disagree.
/// </summary>
internal static class OrderPricing
{
    public const decimal PlatformFeeRate = 0.10m;

    public sealed record Breakdown(Money ItemsTotal, Money DeliveryFee, Money PlatformFee, Money Total);

    public static Breakdown Calculate(IReadOnlyList<(Money UnitPrice, int Quantity)> lines, Money deliveryFee)
    {
        var itemsTotal = lines.Aggregate(Money.Zero(deliveryFee.Currency), (sum, l) => sum + l.UnitPrice.Multiply(l.Quantity));
        var subtotal = itemsTotal + deliveryFee;
        var platformFee = subtotal.Multiply(PlatformFeeRate);
        return new Breakdown(itemsTotal, deliveryFee, platformFee, subtotal + platformFee);
    }
}
