using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Payments.Data;
using FoodDelivery.Modules.Payments.Features.Authorise;
using FoodDelivery.Modules.Payments.Features.CaptureAndVoid;
using FoodDelivery.Modules.Payments.Provider;
using FoodDelivery.Modules.Payments.PublicApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.Modules.Payments;

public sealed class PaymentsModule : IModule
{
    public string Name => "payments";
    public string SchemaSql => Persistence.ReadEmbeddedSql(typeof(PaymentsModule).Assembly, "schema.sql");

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentsDbContext>(o => o
            .UseNpgsql(configuration.ConnectionStringFor(Name))
            .UseSnakeCaseNamingConvention());

        // Choose the ADAPTER plugged into the PORT. One line of config swaps provider.
        if (string.Equals(configuration["Payments:Provider"], "Stripe", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IPaymentProvider, StripePaymentProvider>(c => c.BaseAddress = new Uri("https://api.stripe.com/"));
        else
            services.AddSingleton<IPaymentProvider, FakePaymentProvider>();

        services.AddScoped<Authorise>();
        services.AddScoped<CapturePayment>();
        services.AddScoped<VoidPayment>();
        services.AddScoped<IPaymentsApi, PaymentsApi>();
    }

    // Payments has no HTTP endpoints of its own in Q1: customers pay through PlaceOrder (Ordering),
    // and the card itself is entered in the provider's hosted fields in the browser.
    // (Q3 adds one endpoint here: the provider's webhook.)
    public void MapEndpoints(IEndpointRouteBuilder app) { }
}

/// <summary>
/// The public API implementation is deliberately thin: it just hands each call to its slice.
/// ("public API delegates to slices" - Q1 diagram, page 2.)
/// </summary>
internal sealed class PaymentsApi(Authorise authorise, CapturePayment capture, VoidPayment @void) : IPaymentsApi
{
    public Task<PaymentResult> AuthoriseAsync(Guid orderId, Money amount, string paymentToken, CancellationToken ct) =>
        authorise.HandleAsync(orderId, amount, paymentToken, ct);

    public Task<PaymentResult> CaptureAsync(Guid orderId, CancellationToken ct) => capture.HandleAsync(orderId, ct);

    public Task<PaymentResult> VoidAsync(Guid orderId, CancellationToken ct) => @void.HandleAsync(orderId, ct);
}
