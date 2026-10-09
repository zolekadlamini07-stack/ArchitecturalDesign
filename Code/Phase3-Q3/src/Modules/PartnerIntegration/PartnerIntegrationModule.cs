using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.PartnerIntegration.Adapter;
using FoodDelivery.Modules.PartnerIntegration.Data;
using FoodDelivery.Modules.PartnerIntegration.Features.ForwardOrder;
using FoodDelivery.Modules.PartnerIntegration.Features.PartnerStatus;
using FoodDelivery.Modules.PartnerIntegration.Features.SyncPartnerCatalogue;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.Modules.PartnerIntegration;

/// <summary>
/// FINAL CHALLENGE - the Anti-Corruption Layer, plugged in like every other module.
/// Consistent with our architecture: a MODULE (not a new service), vertical slices inside,
/// ports & adapters at the edge, its own schema, and public APIs only to reach the rest of us.
/// </summary>
public sealed class PartnerIntegrationModule : IModule
{
    public string Name => "partner_integration";
    public string SchemaSql => Persistence.ReadEmbeddedSql(typeof(PartnerIntegrationModule).Assembly, "schema.sql");

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IntegrationStore>();
        services.AddSingleton<PartnerCircuit>();
        services.AddHttpClient();
        // ADAPTER behind its own circuit breaker. (Real adapter: HTTP client, pinned API version.)
        services.AddSingleton<FakePartnerPlatform>();
        services.AddScoped<IPartnerPlatform>(sp => new ResilientPartnerPlatform(sp.GetRequiredService<FakePartnerPlatform>(), sp.GetRequiredService<PartnerCircuit>()));
        services.AddScoped<CatalogueSync>();
        services.AddEventHandler<OrderAwaitingAcceptance, ForwardOrderToPartner>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ReceivePartnerWebhook.Map(app);
        TriggerCatalogueSync.Map(app);
        FakePartnerPlatform.MapSimulator(app); // dev only
    }

    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<CatalogueSyncJob>();
        services.AddHostedService<PartnerInboxProcessor>();
        services.AddHostedService<UnconfirmedPartnerOrderWatchdog>();
    }
}
