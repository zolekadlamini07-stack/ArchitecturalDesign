using FoodDelivery.BuildingBlocks;

namespace FoodDelivery.Modules.Payments.Provider;

// ============================================================================================
// THE PORT (our words, our interface) - Q3 version.
// New since Q1: every money-moving call carries an IDEMPOTENCY KEY, and we can LOOK UP what the
// provider did with a key, and fetch its daily SETTLEMENT report. Those three additions are what
// make the Friday-night incident survivable.
// ============================================================================================
internal interface IPaymentProvider
{
    /// <summary>
    /// Place a hold. Sending the SAME idempotency key again returns the ORIGINAL result and never
    /// charges twice (Stripe, Adyen, Braintree... all support this). Throws on timeout / outage.
    /// </summary>
    Task<ProviderAuthResult> AuthoriseAsync(AuthoriseRequest request, CancellationToken ct);

    /// <summary>"What happened to the request with this key?" - used by reconciliation.</summary>
    Task<ProviderLookupResult> LookupAsync(string idempotencyKey, CancellationToken ct);

    Task CaptureAsync(string providerPaymentId, string idempotencyKey, CancellationToken ct);
    Task VoidAsync(string providerPaymentId, string idempotencyKey, CancellationToken ct);

    /// <summary>The provider's own list of everything it did on a day ("the bank statement").</summary>
    Task<IReadOnlyList<SettlementLine>> GetSettlementReportAsync(DateOnly day, CancellationToken ct);
}

internal sealed record AuthoriseRequest(string IdempotencyKey, string Reference, Money Amount, string PaymentToken);
internal sealed record ProviderAuthResult(bool Authorised, string? ProviderPaymentId, string? DeclineReason);

internal enum ProviderStatus { NotFound, Authorised, Declined, Captured, Voided }
internal sealed record ProviderLookupResult(ProviderStatus Status, string? ProviderPaymentId);

internal sealed record SettlementLine(string Reference, string ProviderPaymentId, ProviderStatus Status, long AmountMinor);

/// <summary>The provider is down or answered with a server error. (A timeout is reported by Polly.)</summary>
internal sealed class ProviderUnavailableException(string message) : Exception(message);
