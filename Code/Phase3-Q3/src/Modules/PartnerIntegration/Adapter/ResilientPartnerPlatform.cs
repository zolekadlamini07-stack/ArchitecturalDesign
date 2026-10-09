using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace FoodDelivery.Modules.PartnerIntegration.Adapter;

/// <summary>
/// The acquired platform is just another EXTERNAL dependency - so it gets exactly the same
/// protection as the payment provider in Q3: timeout + circuit breaker (Challenge.md section 6).
/// Its own breaker, separate from the payments one: their outage must never trip OUR payments.
/// </summary>
internal sealed class ResilientPartnerPlatform(IPartnerPlatform inner, PartnerCircuit circuit) : IPartnerPlatform
{
    public Task<IReadOnlyList<TheirStore>> GetStoresAsync(CancellationToken ct) =>
        circuit.Pipeline.ExecuteAsync(async t => await inner.GetStoresAsync(t), ct).AsTask();

    public Task<TheirOrderCreated> CreateOrderAsync(TheirOrderRequest request, CancellationToken ct) =>
        circuit.Pipeline.ExecuteAsync(async t => await inner.CreateOrderAsync(request, t), ct).AsTask();
}

/// <summary>One breaker per process for the partner (a singleton).</summary>
internal sealed class PartnerCircuit(ILogger<PartnerCircuit> logger)
{
    public ResiliencePipeline Pipeline { get; } = new ResiliencePipelineBuilder()
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5, MinimumThroughput = 3, SamplingDuration = TimeSpan.FromSeconds(60), BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = new PredicateBuilder().Handle<TimeoutRejectedException>().Handle<HttpRequestException>().Handle<PartnerUnavailableException>(),
            OnOpened = _ => { logger.LogWarning("PARTNER CIRCUIT OPEN - acquired platform failing"); return default; },
            OnClosed = _ => { logger.LogInformation("PARTNER CIRCUIT CLOSED - acquired platform healthy"); return default; },
        })
        .AddTimeout(TimeSpan.FromSeconds(5))
        .Build();
}
