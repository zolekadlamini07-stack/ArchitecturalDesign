using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace FoodDelivery.Modules.Payments.Provider;

/// <summary>
/// ADAPTER for Stripe. The ONLY file in the whole system that knows Stripe's URLs, field names and
/// status codes. Stripe's model ("PaymentIntent", "requires_capture") never leaks past this class.
///
/// Uses Stripe's REST API directly with HttpClient, so you can see exactly what is sent:
///   capture_method=manual  -> authorise only (place a hold); we capture later
///   confirm=true           -> try the card immediately
/// </summary>
internal sealed class StripePaymentProvider(HttpClient http, IConfiguration configuration) : IPaymentProvider
{
    private void Authenticate() =>
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", configuration["Payments:Stripe:SecretKey"]);

    public async Task<ProviderAuthResult> AuthoriseAsync(AuthoriseRequest request, CancellationToken ct)
    {
        Authenticate();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["amount"] = request.Amount.ToMinorUnits().ToString(),
            ["currency"] = request.Amount.Currency.ToLowerInvariant(),
            ["payment_method"] = request.PaymentToken,
            ["capture_method"] = "manual",
            ["confirm"] = "true",
            ["metadata[reference]"] = request.Reference,
        });

        // Q1: no Idempotency-Key header is sent. If this call times out and the customer retries,
        // Stripe treats the retry as a brand-new charge. That is Risk R2, fixed in Q3.
        using var response = await http.PostAsync("v1/payment_intents", form, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;

        if (!response.IsSuccessStatusCode)
            return new ProviderAuthResult(false, null, root.GetProperty("error").GetProperty("code").GetString());

        var status = root.GetProperty("status").GetString();
        return status == "requires_capture"
            ? new ProviderAuthResult(true, root.GetProperty("id").GetString(), null)
            : new ProviderAuthResult(false, root.GetProperty("id").GetString(), status);
    }

    public async Task CaptureAsync(string providerPaymentId, CancellationToken ct)
    {
        Authenticate();
        using var response = await http.PostAsync($"v1/payment_intents/{providerPaymentId}/capture", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task VoidAsync(string providerPaymentId, CancellationToken ct)
    {
        Authenticate();
        // Stripe calls releasing a hold "cancel".
        using var response = await http.PostAsync($"v1/payment_intents/{providerPaymentId}/cancel", null, ct);
        response.EnsureSuccessStatusCode();
    }
}
