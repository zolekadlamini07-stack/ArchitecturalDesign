using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Delivery.PublicApi;
using FoodDelivery.Modules.Ordering.Data;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.Payments.PublicApi;
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

        services.AddOutboxFor(Name);
        services.AddScoped<IOrderingApi, Features.PartnerUpdates.ApplyPartnerUpdate>(); // challenge

        // Delivery facts (since Q1/Q2)
        services.AddEventHandler<DeliveryPickedUp, Features.UpdateOrderStatus.WhenDeliveryPickedUp>();
        services.AddEventHandler<DeliveryCompleted, Features.UpdateOrderStatus.WhenDeliveryCompleted>();
        // Q3: payment facts
        services.AddEventHandler<PaymentAuthorised, Features.PaymentOutcome.WhenPaymentAuthorised>();
        services.AddEventHandler<PaymentDeclined, Features.PaymentOutcome.WhenPaymentDeclined>();
    }

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
        Features.GetOrderTimeline.GetOrderTimeline.Map(app);   // Q3 new (support)
    }

    /// <summary>Q3: the "give up after 10 minutes" check runs only in the Worker.</summary>
    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddHostedService<Features.PaymentOutcome.ExpireUnconfirmedPayments>();
}
