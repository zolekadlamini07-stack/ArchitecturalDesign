using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace FoodDelivery.Modules.Restaurants.Caching;

// =============================================================================================
// Q2 CHANGE 1 - REDIS CACHE, CACHE-ASIDE (D11). Solves problem P1: browsing gets most traffic.
//
// CACHE-ASIDE: 1) look in the cache  2) on a miss, read PostgreSQL  3) store the result for 5 min.
// Analogy: the specials board by the door - read the board instead of walking to the back office.
//
// Why Redis and not in-process memory? We now run 2+ API instances (rolling deploys). An in-memory
// cache would exist once PER instance, and "delete this key" would only clear one of them. Redis is
// shared: one copy, one delete.
//
// INVALIDATION (the hard part), two layers:
//   - the slices that CHANGE menus delete the affected key right after saving
//   - every entry also expires after 5 minutes (TTL) as a safety net if a delete is ever missed
//
// SAFETY RULE: the cache is for DISPLAY only. PlaceOrder prices from the database (IRestaurantsApi).
// =============================================================================================
internal sealed class RestaurantCache(IDistributedCache cache)
{
    private static readonly DistributedCacheEntryOptions FiveMinutes = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    public static string MenuKey(Guid restaurantId) => $"menu:{restaurantId}";

    // The browse list can be cached under MANY keys (search text, page...). Deleting them one by one
    // is impossible, so every list key includes a "version". Invalidating = writing a new version;
    // the old keys are simply never read again and expire on their own.
    private const string ListVersionKey = "restaurants:list:version";

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> loadFromDatabase, CancellationToken ct)
    {
        var cached = await cache.GetStringAsync(key, ct);
        if (cached is not null) return JsonSerializer.Deserialize<T>(cached)!;   // HIT: no database work

        var value = await loadFromDatabase();                                     // MISS: read PostgreSQL...
        await cache.SetStringAsync(key, JsonSerializer.Serialize(value), FiveMinutes, ct); // ...and fill the cache
        return value;
    }

    public async Task<string> ListKeyAsync(string filters, CancellationToken ct)
    {
        var version = await cache.GetStringAsync(ListVersionKey, ct) ?? "0";
        return $"restaurants:list:{version}:{filters}";
    }

    /// <summary>Call AFTER saving a menu change - "wipe the board".</summary>
    public Task InvalidateMenuAsync(Guid restaurantId, CancellationToken ct) => cache.RemoveAsync(MenuKey(restaurantId), ct);

    /// <summary>Call AFTER saving a change that affects the browse list (open/close, approval).</summary>
    public Task InvalidateListAsync(CancellationToken ct) => cache.SetStringAsync(ListVersionKey, Guid.NewGuid().ToString("N"), ct);
}
