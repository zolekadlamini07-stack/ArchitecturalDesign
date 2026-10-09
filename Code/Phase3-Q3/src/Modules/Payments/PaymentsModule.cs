using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.Payments.Data;
using FoodDelivery.Modules.Payments.Features.Authorise;
using FoodDelivery.Modules.Payments.Features.CaptureAndVoid;
using FoodDelivery.Modules.Payments.Features.Reconciliation;
using FoodDelivery.Modules.Payments.Provider;
using FoodDelivery.Modules.Payments.PublicApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodDelivery.Modules.Payments;

// ============================================================================================
// PAYMENTS MODULE - Q3. Still a MODULE inside the monolith (D23: we did NOT extract it).
// Every Q3 fix (ledger, idempotency, outbox, breaker, reconciliation) works the same in-process,
// and extraction would ADD a network hop that can also time out ambiguously.
// It IS extraction-ready: own schema, events only, contract in Payments.Contracts.
// ============================================================================================
public sealed class PaymentsModule : IModule
{
    public string Name => "payments";
    public string SchemaSql => Persistence.ReadEmbeddedSql(typeof(PaymentsModule).Assembly, "schema.sql");

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentsDbContext>(o => o
            .UseNpgsql(configuration.ConnectionStringFor(Name))
            .UseSnakeCaseNamingConvention());

        // ADAPTER (Stripe or the Friday-night fake) wrapped in TIMEOUT + CIRCUIT BREAKER.
        // The pipeline is a SINGLETON so every job in this process shares one breaker state.
        services.AddHttpClient();
        services.AddSingleton(sp => ResilientPaymentProvider.BuildPipeline(sp.GetRequiredService<ILoggerFactory>().CreateLogger("PaymentProviderCircuit")));
        if (string.Equals(configuration["Payments:Provider"], "Stripe", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<StripePaymentProvider>(c => c.BaseAddress = new Uri("https://api.stripe.com/"));
            services.AddScoped<IPaymentProvider>(sp => new ResilientPaymentProvider(sp.GetRequiredService<StripePaymentProvider>(), sp.GetRequiredService<Polly.ResiliencePipeline>()));
        }
        else
        {
            services.AddSingleton<FakePaymentProvider>();
            services.AddScoped<IPaymentProvider>(sp => new ResilientPaymentProvider(sp.GetRequiredService<FakePaymentProvider>(), sp.GetRequiredService<Polly.ResiliencePipeline>()));
        }

        services.AddScoped<ReleaseHold>();
        services.AddScoped<IPaymentsApi, PaymentsApi>();

        // Q3: Payments publishes PaymentAuthorised / PaymentDeclined through its outbox...
        services.AddOutboxFor(Name);
        // ...and REACTS to Ordering's events, in the Worker's 'payments' lane (bulkhead).
        services.AddEventHandler<OrderPlaced, AuthoriseOnOrderPlaced>();
        services.AddEventHandler<OrderAccepted, CaptureOnOrderAccepted>();
        services.AddEventHandler<OrderRejected, VoidOnOrderRejected>();
        services.AddEventHandler<OrderPaymentAbandoned, VoidOnOrderPaymentAbandoned>();
    }

    /// <summary>Q3: Payments' only HTTP endpoint is for the PROVIDER to call us (webhooks).</summary>
    public void MapEndpoints(IEndpointRouteBuilder app) => ReceiveProviderWebhook.Map(app);

    /// <summary>Q3: reconciliation layers 2 and 3 run only in the Worker.</summary>
    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<ReconciliationSweeper>();
        services.AddHostedService<DailySettlementCheck>();
    }
}

/// <summary>Read-only support view over the append-only payment log.</summary>
internal sealed class PaymentsApi(PaymentsDbContext db) : IPaymentsApi
{
    public async Task<IReadOnlyList<PaymentTimelineEntry>> GetTimelineAsync(Guid orderId, CancellationToken ct) =>
        await db.Log.AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .OrderBy(l => l.At)
            .Select(l => new PaymentTimelineEntry(l.At, l.AttemptId, l.What, l.Detail))
            .ToListAsync(ct);
}
