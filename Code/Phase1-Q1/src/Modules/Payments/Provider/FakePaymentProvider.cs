namespace FoodDelivery.Modules.Payments.Provider;

/// <summary>
/// ADAPTER used for local development: behaves like a payment provider without a real account.
///   payment token "tok_decline" -> declined (insufficient funds)
///   anything else               -> authorised
/// Because Payments depends on the IPaymentProvider PORT, swapping this in is one line of config.
/// </summary>
internal sealed class FakePaymentProvider : IPaymentProvider
{
    public async Task<ProviderAuthResult> AuthoriseAsync(AuthoriseRequest request, CancellationToken ct)
    {
        await Task.Delay(150, ct); // pretend network latency
        return request.PaymentToken == "tok_decline"
            ? new ProviderAuthResult(false, null, "insufficient_funds")
            : new ProviderAuthResult(true, "fake_pi_" + Guid.NewGuid().ToString("N")[..12], null);
    }

    public Task CaptureAsync(string providerPaymentId, CancellationToken ct) => Task.Delay(100, ct);

    public Task VoidAsync(string providerPaymentId, CancellationToken ct) => Task.Delay(100, ct);
}
