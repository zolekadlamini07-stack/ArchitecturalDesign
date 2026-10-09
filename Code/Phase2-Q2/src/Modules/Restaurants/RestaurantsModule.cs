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
internal sealed class RestaurantsApi(RestaurantsDbContext db) : IRestaurantsApi
{
    public async Task<RestaurantInfo?> GetRestaurantAsync(Guid restaurantId, CancellationToken ct) =>
        await db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new RestaurantInfo(r.Id, r.Name, r.IsOpen, new Money(r.DeliveryFee, r.Currency)))
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<PricedItem>> GetPricedItemsAsync(Guid restaurantId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        var currency = await db.Restaurants.Where(r => r.Id == restaurantId).Select(r => r.Currency).SingleOrDefaultAsync(ct) ?? "ZAR";
        return await db.MenuItems.AsNoTracking()
            .Where(i => i.RestaurantId == restaurantId && itemIds.Contains(i.Id))
            .Select(i => new PricedItem(i.Id, i.Name, new Money(i.Price, currency), i.IsAvailable))
            .ToListAsync(ct);
    }
}
