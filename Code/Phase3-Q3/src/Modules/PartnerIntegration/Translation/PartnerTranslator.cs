using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.PartnerIntegration.Adapter;
using FoodDelivery.Modules.Restaurants.PublicApi;

namespace FoodDelivery.Modules.PartnerIntegration.Translation;

// ============================================================================================
// MODEL TRANSLATION - every mismatch between their model and ours is absorbed HERE, in one place,
// and nowhere else (Challenge.md section 5).
//
//   ids          int store_id / string sku      -> our Guids, via the id_map (never leaked)
//   money        price_cents: 7500              -> Money(75.00, "ZAR")
//   availability in_stock 0/1, sometimes MISSING -> missing = NOT available (safe default:
//                                                   never sell what might not exist)
//   menu shape   sections -> products           -> flat list of items
//   statuses     their words                    -> our words, via an EXPLICIT table.
//                An UNKNOWN status is an ALERT, never a guess.
// ============================================================================================
internal static class PartnerTranslator
{
    public const string Currency = "ZAR";

    public static Money FromCents(int cents) => new(cents / 100m, Currency);

    public static PartnerRestaurantSnapshot ToOurRestaurant(TheirStore store, Guid ourRestaurantId, Func<string, Guid> ourItemIdForSku) =>
        new(ourRestaurantId,
            store.StoreName,
            store.Street,
            store.Town,
            store.AcceptingOrders,
            FromCents(store.DeliveryCents),
            store.Sections.SelectMany(s => s.Products)
                .Select(p => new PartnerMenuItem(ourItemIdForSku(p.Sku), p.Label, FromCents(p.PriceCents), IsAvailable: p.InStock == 1))
                .ToList());

    /// <summary>THE STATUS MAPPING TABLE. Anything not listed is rejected loudly.</summary>
    private static readonly Dictionary<string, PartnerOrderUpdate> StatusMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CONFIRMED"] = PartnerOrderUpdate.Accepted,
        ["COOKING"] = PartnerOrderUpdate.Preparing,
        ["DISPATCHED"] = PartnerOrderUpdate.OutForDelivery,
        ["DONE"] = PartnerOrderUpdate.Delivered,
        ["CANCELLED"] = PartnerOrderUpdate.Rejected, // before confirmation -> rejected -> payment VOIDED
    };

    public static PartnerOrderUpdate ToOurUpdate(string theirStatus) =>
        StatusMap.TryGetValue(theirStatus, out var ours)
            ? ours
            : throw new UnknownPartnerStatusException(theirStatus);
}

internal sealed class UnknownPartnerStatusException(string status)
    : Exception($"Unknown partner status '{status}' - not guessing. Add it to the mapping table after checking with the partner.");
