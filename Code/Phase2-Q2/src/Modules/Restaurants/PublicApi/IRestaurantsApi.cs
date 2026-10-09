using FoodDelivery.BuildingBlocks;

// ============================================================================================
// PUBLIC API of the Restaurants module (restaurant profile + menu).
// Q2 adds one public EVENT: RestaurantApproved (self-service onboarding).
// ============================================================================================
namespace FoodDelivery.Modules.Restaurants.PublicApi;

public interface IRestaurantsApi
{
    Task<RestaurantInfo?> GetRestaurantAsync(Guid restaurantId, CancellationToken ct);

    /// <summary>
    /// Real prices for an order. ALWAYS read from the DATABASE, never the cache (Q2 safety rule):
    /// the cache may be up to 5 minutes stale, which is fine for DISPLAY but never for CHARGING.
    /// </summary>
    Task<IReadOnlyList<PricedItem>> GetPricedItemsAsync(Guid restaurantId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct);
}

public sealed record RestaurantInfo(Guid RestaurantId, string Name, bool IsOpen, Money DeliveryFee);

public sealed record PricedItem(Guid ItemId, string Name, Money Price, bool IsAvailable);

/// <summary>
/// Q2: an admin approved a restaurant that applied by itself. Identity listens and creates the
/// restaurant's staff login. Restaurants doesn't know Identity exists - it just announces the fact.
/// </summary>
public sealed record RestaurantApproved(Guid RestaurantId, string RestaurantName, string ContactEmail) : DomainEvent;
