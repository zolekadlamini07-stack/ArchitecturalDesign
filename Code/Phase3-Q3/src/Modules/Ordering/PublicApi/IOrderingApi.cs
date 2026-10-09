// ============================================================================================
// PUBLIC API of the Ordering module (commands) - added for the FINAL CHALLENGE.
//
// Orders placed with the ACQUIRED company's restaurants are accepted, cooked and delivered in
// THEIR systems. The PartnerIntegration module (the anti-corruption layer) translates their status
// updates into OUR language and calls this interface. Ordering never learns their model exists.
// ============================================================================================
namespace FoodDelivery.Modules.Ordering.PublicApi;

public interface IOrderingApi
{
    /// <summary>
    /// Apply a status change reported by the partner, for a PARTNER-fulfilled order.
    /// Idempotent and order-safe: a repeated update, or one that would go BACKWARDS (e.g. "cooking"
    /// arriving after "dispatched"), is ignored and returns false.
    /// </summary>
    Task<bool> ApplyPartnerUpdateAsync(Guid orderId, PartnerOrderUpdate update, string? reason, CancellationToken ct);

    /// <summary>What the integration needs to forward a PARTNER order (our ids only - it maps them to theirs).</summary>
    Task<OrderForForwarding?> GetOrderForForwardingAsync(Guid orderId, CancellationToken ct);
}

public sealed record OrderForForwarding(Guid OrderId, Guid RestaurantId, string DeliveryAddress, decimal Total, string Currency, IReadOnlyList<OrderLineForForwarding> Lines);
public sealed record OrderLineForForwarding(Guid MenuItemId, int Quantity);

/// <summary>Our words for what can happen to a partner order (after translation).</summary>
public enum PartnerOrderUpdate { Accepted, Rejected, Preparing, OutForDelivery, Delivered }
