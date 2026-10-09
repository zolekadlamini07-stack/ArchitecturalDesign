using FoodDelivery.BuildingBlocks;

// ============================================================================================
// PUBLIC API of the Restaurants module (restaurant profile + menu).
//
// Why is Menu inside Restaurants and not its own module? (CHANGES C3)
// Same owner (the restaurant), same reasons to change, and the same read path
// ("show me this restaurant and its menu"). Splitting them would add a cross-module call on the
// busiest read in the system for no benefit.
// ============================================================================================
namespace FoodDelivery.Modules.Restaurants.PublicApi;

public interface IRestaurantsApi
{
    /// <summary>Basic facts Ordering needs: does it exist, is it open, what is the delivery fee.</summary>
    Task<RestaurantInfo?> GetRestaurantAsync(Guid restaurantId, CancellationToken ct);

    /// <summary>
    /// Current names, prices and availability for the requested menu items.
    /// Ordering calls this when an order is placed so that THE SERVER decides prices - prices sent
    /// by the client are never trusted (trust boundary B5).
    /// </summary>
    Task<IReadOnlyList<PricedItem>> GetPricedItemsAsync(Guid restaurantId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct);
}

public sealed record RestaurantInfo(Guid RestaurantId, string Name, bool IsOpen, Money DeliveryFee);

public sealed record PricedItem(Guid ItemId, string Name, Money Price, bool IsAvailable);
