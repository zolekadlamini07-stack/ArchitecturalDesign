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
    }
}

/// <summary>
/// Internal implementation of the public API. Always reads the DATABASE (never a cache), because
/// these answers are used to charge money.
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
