using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Delivery.PublicApi;
using FoodDelivery.Modules.Ordering.Data;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.Modules.Ordering;

public sealed class OrderingModule : IModule
{
    public string Name => "ordering";
    public string SchemaSql => Persistence.ReadEmbeddedSql(typeof(OrderingModule).Assembly, "schema.sql");

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrderingDbContext>(o => o
            .UseNpgsql(configuration.ConnectionStringFor(Name))
            .UseSnakeCaseNamingConvention());

        // Subscriptions to Delivery's public events.
        services.AddScoped<IDomainEventHandler<DeliveryPickedUp>, Features.UpdateOrderStatus.WhenDeliveryPickedUp>();
        services.AddScoped<IDomainEventHandler<DeliveryCompleted>, Features.UpdateOrderStatus.WhenDeliveryCompleted>();
    }

    /// <summary>One line per vertical slice. Reading this list = reading what the module does.</summary>
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        Features.QuoteBasket.QuoteBasket.Map(app);
        Features.PlaceOrder.PlaceOrder.Map(app);
        Features.Queries.TrackOrder.Map(app);
        Features.Queries.ListMyOrders.Map(app);
        Features.Queries.ListIncomingOrders.Map(app);
        Features.Queries.ListRestaurantOrderHistory.Map(app);
        Features.AcceptOrder.AcceptOrder.Map(app);
        Features.AcceptOrder.RejectOrder.Map(app);
        Features.UpdateOrderStatus.MarkPreparing.Map(app);
        Features.UpdateOrderStatus.MarkReadyForPickup.Map(app);
    }
}
