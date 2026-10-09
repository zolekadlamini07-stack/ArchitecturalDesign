using FoodDelivery.BuildingBlocks;

namespace FoodDelivery.Modules.Payments.Provider;

// ============================================================================================
// PORTS AND ADAPTERS (Hexagonal architecture, used ONLY at the edges - D2/D6)
//
// This interface is the PORT: it describes what WE need from a payment provider, in OUR words
// (Money, "authorise", "capture"). It is 'internal' - even other modules can't see it.
//
// Each provider gets an ADAPTER that implements this port in the vendor's language:
//   StripePaymentProvider  -> real Stripe HTTP API
//   FakePaymentProvider    -> in-memory stand-in for local development and tests
// Switching provider = writing one new adapter. Nothing outside this folder changes.
// Analogy: a travel plug adapter - the laptop (our code) doesn't care which country's socket it is.
// ============================================================================================
internal interface IPaymentProvider
{
    Task<ProviderAuthResult> AuthoriseAsync(AuthoriseRequest request, CancellationToken ct);
    Task CaptureAsync(string providerPaymentId, CancellationToken ct);
    Task VoidAsync(string providerPaymentId, CancellationToken ct);
}

/// <param name="Reference">Our own reference (the order id), stored in the provider's metadata.</param>
internal sealed record AuthoriseRequest(string Reference, Money Amount, string PaymentToken);

internal sealed record ProviderAuthResult(bool Authorised, string? ProviderPaymentId, string? DeclineReason);
