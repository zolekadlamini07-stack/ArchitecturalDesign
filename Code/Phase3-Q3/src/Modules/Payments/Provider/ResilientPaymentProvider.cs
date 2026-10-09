using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace FoodDelivery.Modules.Payments.Provider;

// ============================================================================================
// Q3 CHANGE C4 - TIMEOUT + CIRCUIT BREAKER around the provider (D21).
//
// This class WRAPS whichever adapter is configured (decorator pattern), so Stripe and the fake
// both get the same protection without knowing about it.
//
// TIMEOUT (10 s): never wait forever. A timeout means UNKNOWN, not "failed".
//
// CIRCUIT BREAKER - like the breaker in your house:
//   CLOSED    normal. Failures are counted.
//   OPEN      >= 50% of calls failed (min 4 calls in 30 s): STOP calling for 30 s. Calls fail
//             instantly with BrokenCircuitException - no 10-second waits, no extra load on a
//             provider that is already struggling.
//   HALF-OPEN after 30 s, let one trial call through. Success -> CLOSED. Failure -> OPEN again.
// It recovers BY ITSELF when the provider recovers - nobody flips a switch at 2am.
//
// Note what is NOT here: automatic retries. Retrying a payment is only safe with the SAME
// idempotency key, at a controlled pace - so retries are done by the job queue and the
// reconciliation sweeper, never blindly inside this call.
// ============================================================================================
internal sealed class ResilientPaymentProvider(IPaymentProvider inner, ResiliencePipeline pipeline) : IPaymentProvider
{
    public Task<ProviderAuthResult> AuthoriseAsync(AuthoriseRequest request, CancellationToken ct) =>
        pipeline.ExecuteAsync(async t => await inner.AuthoriseAsync(request, t), ct).AsTask();

    public Task<ProviderLookupResult> LookupAsync(string idempotencyKey, CancellationToken ct) =>
        pipeline.ExecuteAsync(async t => await inner.LookupAsync(idempotencyKey, t), ct).AsTask();

    public Task CaptureAsync(string providerPaymentId, string idempotencyKey, CancellationToken ct) =>
        pipeline.ExecuteAsync(async t => await inner.CaptureAsync(providerPaymentId, idempotencyKey, t), ct).AsTask();

    public Task VoidAsync(string providerPaymentId, string idempotencyKey, CancellationToken ct) =>
        pipeline.ExecuteAsync(async t => await inner.VoidAsync(providerPaymentId, idempotencyKey, t), ct).AsTask();

    public Task<IReadOnlyList<SettlementLine>> GetSettlementReportAsync(DateOnly day, CancellationToken ct) =>
        inner.GetSettlementReportAsync(day, ct); // batch, off the critical path: no breaker needed

    /// <summary>Builds the shared pipeline. ONE instance per process, so every caller sees the same breaker state.</summary>
    public static ResiliencePipeline BuildPipeline(ILogger logger) =>
        new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder()
                    .Handle<TimeoutRejectedException>()
                    .Handle<HttpRequestException>()
                    .Handle<ProviderUnavailableException>(),
                OnOpened = _ => { logger.LogWarning("CIRCUIT OPEN: payment provider failing - pausing calls for 30 s"); return default; },
                OnHalfOpened = _ => { logger.LogInformation("CIRCUIT HALF-OPEN: sending a trial call to the payment provider"); return default; },
                OnClosed = _ => { logger.LogInformation("CIRCUIT CLOSED: payment provider healthy again"); return default; },
            })
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();
}
