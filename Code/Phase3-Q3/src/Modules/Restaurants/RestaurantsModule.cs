using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Restaurants.Data;
using FoodDelivery.Modules.Restaurants.PublicApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.Modules.Restaurants;

public sealed class RestaurantsModule : IModule
{
    public string Name => "restaurants";
    public string SchemaSql => Persistence.ReadEmbeddedSql(typeof(RestaurantsModule).Assembly, "schema.sql");

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RestaurantsDbContext>(o => o
            .UseNpgsql(configuration.ConnectionStringFor(Name))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IRestaurantsApi, RestaurantsApi>();

        // Q2: shared Redis cache for browse/menu (D11). Connection string "Redis".
        services.AddStackExchangeRedisCache(o => o.Configuration = configuration.GetConnectionString("Redis"));
        services.AddScoped<Caching.RestaurantCache>();

        // Q2: publishes RestaurantApproved through its outbox.
        services.AddOutboxFor(Name);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        Features.Browse.BrowseRestaurants.Map(app);
        Features.Browse.GetMenu.Map(app);
        Features.ManageRestaurant.CreateRestaurant.Map(app);
        Features.ManageRestaurant.SetOpenStatus.Map(app);
        Features.ManageMenu.AddMenuItem.Map(app);
        Features.ManageMenu.UpdateMenuItem.Map(app);
        Features.ManageMenu.SetItemAvailability.Map(app);
        Features.Onboarding.ApplyAsRestaurant.Map(app);   // Q2 new
        Features.Onboarding.ApproveRestaurant.Map(app);   // Q2 new
    }
}

/// <summary>
/// Internal implementation of the public API. Q2 SAFETY RULE: always reads the DATABASE, never the
/// cache, because these answers are used to charge money.
/// </summary>
internal sealed class RestaurantsApi(RestaurantsDbContext db, Caching.RestaurantCache cache) : IRestaurantsApi
{
    public async Task<RestaurantInfo?> GetRestaurantAsync(Guid restaurantId, CancellationToken ct) =>
        await db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new RestaurantInfo(r.Id, r.Name, r.IsOpen, new Money(r.DeliveryFee, r.Currency), r.Source == "PARTNER"))
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<PricedItem>> GetPricedItemsAsync(Guid restaurantId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        var currency = await db.Restaurants.Where(r => r.Id == restaurantId).Select(r => r.Currency).SingleOrDefaultAsync(ct) ?? "ZAR";
        return await db.MenuItems.AsNoTracking()
            .Where(i => i.RestaurantId == restaurantId && itemIds.Contains(i.Id))
            .Select(i => new PricedItem(i.Id, i.Name, new Money(i.Price, currency), i.IsAvailable))
            .ToListAsync(ct);
    }

    /// <summary>Challenge: refresh the read-only PARTNER copy. Their system is the owner; we just mirror it.</summary>
    public async Task UpsertPartnerRestaurantAsync(PartnerRestaurantSnapshot snapshot, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.SingleOrDefaultAsync(r => r.Id == snapshot.RestaurantId, ct);
        if (restaurant is null)
        {
            restaurant = new Data.Restaurant { Id = snapshot.RestaurantId, Source = "PARTNER", ApprovalStatus = Data.ApprovalStatus.Active, CreatedAt = DateTimeOffset.UtcNow };
            db.Restaurants.Add(restaurant);
        }
        restaurant.Name = snapshot.Name;
        restaurant.Address = snapshot.Address;
        restaurant.City = snapshot.City;
        restaurant.IsOpen = snapshot.IsOpen;
        restaurant.DeliveryFee = snapshot.DeliveryFee.Amount;
        restaurant.Currency = snapshot.DeliveryFee.Currency;

        var existing = await db.MenuItems.Where(i => i.RestaurantId == snapshot.RestaurantId).ToDictionaryAsync(i => i.Id, ct);
        foreach (var item in snapshot.Items)
        {
            if (!existing.TryGetValue(item.ItemId, out var row))
                db.MenuItems.Add(row = new Data.MenuItem { Id = item.ItemId, RestaurantId = snapshot.RestaurantId });
            row.Name = item.Name;
            row.Price = item.Price.Amount;
            row.IsAvailable = item.IsAvailable;
        }
        foreach (var gone in existing.Values.Where(e => snapshot.Items.All(i => i.ItemId != e.Id)))
            gone.IsAvailable = false; // removed on their side -> no longer sellable here

        await db.SaveChangesAsync(ct);
        await cache.InvalidateMenuAsync(snapshot.RestaurantId, ct);
        await cache.InvalidateListAsync(ct);
    }
}
