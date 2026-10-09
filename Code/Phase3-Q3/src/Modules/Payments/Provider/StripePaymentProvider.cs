using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace FoodDelivery.Modules.Payments.Provider;

/// <summary>
/// ADAPTER for Stripe - Q3 version. Compare with Phase 1: every money-moving call now sends an
/// Idempotency-Key header. Stripe stores the result under that key for at least 24 hours, so a
/// retry with the same key returns the original answer and NEVER charges twice.
/// Our attempt id is also stored in metadata[reference], so we can search for it later.
/// </summary>
internal sealed class StripePaymentProvider(HttpClient http, IConfiguration configuration) : IPaymentProvider
{
    private HttpRequestMessage Request(HttpMethod method, string path, string? idempotencyKey, HttpContent? body = null)
    {
        var message = new HttpRequestMessage(method, path) { Content = body };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["Payments:Stripe:SecretKey"]);
        if (idempotencyKey is not null) message.Headers.Add("Idempotency-Key", idempotencyKey); // <-- Q3
        return message;
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        using var response = await http.SendAsync(message, ct);
        // 5xx / 429 = provider trouble -> counts towards the circuit breaker. 4xx = a real answer.
        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new ProviderUnavailableException($"Stripe returned {(int)response.StatusCode}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement.Clone();
    }

    public async Task<ProviderAuthResult> AuthoriseAsync(AuthoriseRequest request, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["amount"] = request.Amount.ToMinorUnits().ToString(),
            ["currency"] = request.Amount.Currency.ToLowerInvariant(),
            ["payment_method"] = request.PaymentToken,
            ["capture_method"] = "manual",
            ["confirm"] = "true",
            ["metadata[reference]"] = request.Reference,
        });
        var json = await SendAsync(Request(HttpMethod.Post, "v1/payment_intents", request.IdempotencyKey, form), ct);
        if (json.TryGetProperty("error", out var error))
            return new ProviderAuthResult(false, null, error.GetProperty("code").GetString());
        var status = json.GetProperty("status").GetString();
        return new ProviderAuthResult(status == "requires_capture", json.GetProperty("id").GetString(), status == "requires_capture" ? null : status);
    }

    public async Task<ProviderLookupResult> LookupAsync(string idempotencyKey, CancellationToken ct)
    {
        // Search by the reference we stored in metadata (works beyond the 24 h idempotency window).
        var query = Uri.EscapeDataString($"metadata['reference']:'{idempotencyKey}'");
        var json = await SendAsync(Request(HttpMethod.Get, $"v1/payment_intents/search?query={query}", null), ct);
        var hit = json.GetProperty("data").EnumerateArray().FirstOrDefault();
        if (hit.ValueKind == JsonValueKind.Undefined) return new ProviderLookupResult(ProviderStatus.NotFound, null);
        return new ProviderLookupResult(Map(hit.GetProperty("status").GetString()), hit.GetProperty("id").GetString());
    }

    public async Task CaptureAsync(string providerPaymentId, string idempotencyKey, CancellationToken ct) =>
        await SendAsync(Request(HttpMethod.Post, $"v1/payment_intents/{providerPaymentId}/capture", idempotencyKey), ct);

    public async Task VoidAsync(string providerPaymentId, string idempotencyKey, CancellationToken ct) =>
        await SendAsync(Request(HttpMethod.Post, $"v1/payment_intents/{providerPaymentId}/cancel", idempotencyKey), ct);

    public async Task<IReadOnlyList<SettlementLine>> GetSettlementReportAsync(DateOnly day, CancellationToken ct)
    {
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var json = await SendAsync(Request(HttpMethod.Get, $"v1/payment_intents?limit=100&created[gte]={from}&created[lt]={from + 86400}", null), ct);
        return json.GetProperty("data").EnumerateArray().Select(pi => new SettlementLine(
            pi.GetProperty("metadata").TryGetProperty("reference", out var r) ? r.GetString() ?? "" : "",
            pi.GetProperty("id").GetString()!, Map(pi.GetProperty("status").GetString()), pi.GetProperty("amount").GetInt64())).ToList();
    }

    private static ProviderStatus Map(string? stripeStatus) => stripeStatus switch
    {
        "requires_capture" => ProviderStatus.Authorised,
        "succeeded" => ProviderStatus.Captured,
        "canceled" => ProviderStatus.Voided,
        "requires_payment_method" => ProviderStatus.Declined,
        _ => ProviderStatus.NotFound,
    };
}
