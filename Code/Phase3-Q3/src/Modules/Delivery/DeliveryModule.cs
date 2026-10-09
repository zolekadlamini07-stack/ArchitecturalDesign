using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Delivery.Data;
using FoodDelivery.Modules.Delivery.PublicApi;
using FoodDelivery.Modules.Identity.PublicApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.Modules.Delivery;

public sealed class DeliveryModule : IModule
{
    public string Name => "delivery";
    public string SchemaSql => Persistence.ReadEmbeddedSql(typeof(DeliveryModule).Assembly, "schema.sql");

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DeliveryDbContext>(o => o
            .UseNpgsql(configuration.ConnectionStringFor(Name))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<Features.RequestDelivery.RequestDelivery>();
        services.AddScoped<IDeliveryApi, DeliveryApi>();
        // Q2: publishes DeliveryPickedUp / DeliveryCompleted via its outbox; handlers run in the Worker.
        services.AddOutboxFor(Name);
        services.AddEventHandler<DriverAccountCreated, Features.Shifts.CreateDriverProfile>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        Features.Shifts.StartShift.Map(app);
        Features.Shifts.EndShift.Map(app);
        Features.ListDeliveryRequests.ListDeliveryRequests.Map(app);
        Features.AcceptDelivery.AcceptDelivery.Map(app);
        Features.UpdateDeliveryStatus.MarkPickedUp.Map(app);
        Features.UpdateDeliveryStatus.MarkDelivered.Map(app);
        Features.UpdateDeliveryStatus.ListMyDeliveries.Map(app);
    }
}

internal sealed class DeliveryApi(Features.RequestDelivery.RequestDelivery requestDelivery) : IDeliveryApi
{
    public Task RequestDeliveryAsync(Guid orderId, Guid restaurantId, string deliveryAddress, CancellationToken ct) =>
        requestDelivery.HandleAsync(orderId, restaurantId, deliveryAddress, ct);
}
