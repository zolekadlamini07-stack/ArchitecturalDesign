using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoodDelivery.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FoodDelivery.Modules.Payments.Provider;

// ============================================================================================
// FAKE PROVIDER THAT CAN SIMULATE FRIDAY NIGHT (development only).
//
// It behaves like a real provider: it keeps its own records (in the fake_provider schema, standing
// in for Stripe's database), honours idempotency keys, answers lookups, sends webhooks and produces
// a settlement report. Switch its behaviour at runtime with feature flags:
//
//   fake-provider-down                   every call fails with a 503        -> circuit breaker opens
//   fake-provider-timeout-but-succeeds   the charge SUCCEEDS but the reply never arrives in time
//                                        -> our attempt becomes UNKNOWN, then a webhook / the sweeper
//                                           discovers it actually succeeded
//   fake-provider-slow                   3 s per call (slow but working)
//
//   PUT /admin/feature-flags/fake-provider-timeout-but-succeeds?enabled=true
//
// Payment token "tok_decline" always declines.
// ============================================================================================
internal sealed class FakePaymentProvider(IConfiguration configuration, IFeatureFlags flags, IHttpClientFactory http, ILogger<FakePaymentProvider> logger)
    : IPaymentProvider
{
    private readonly NpgsqlDataSource _db = NpgsqlDataSource.Create(configuration.ConnectionStringFor("Default"));

    public async Task<ProviderAuthResult> AuthoriseAsync(AuthoriseRequest request, CancellationToken ct)
    {
        await SimulateTroubleAsync(ct);

        // IDEMPOTENCY: same key -> return the ORIGINAL result, never charge twice.
        var existing = await FindAsync(request.IdempotencyKey, ct);
        if (existing is not null)
        {
            logger.LogInformation("FAKE PROVIDER: replay of key {Key} -> returning original result, NO new charge", request.IdempotencyKey);
            return new ProviderAuthResult(existing.Value.Status == "authorised", existing.Value.ProviderPaymentId,
                existing.Value.Status == "declined" ? "card_declined" : null);
        }

        var declined = request.PaymentToken == "tok_decline";
        var providerPaymentId = "fake_pi_" + Guid.NewGuid().ToString("N")[..12];
        await InsertAsync(request, providerPaymentId, declined ? "declined" : "authorised", ct);

        if (!declined && await flags.IsEnabledAsync("fake-provider-timeout-but-succeeds", ct))
        {
            // THE FRIDAY-NIGHT CASE: the money IS held, but our caller won't hear about it in time.
            logger.LogWarning("FAKE PROVIDER: authorised {Key} but the reply will be LOST (timeout)", request.IdempotencyKey);
            _ = SendWebhookLaterAsync(request.IdempotencyKey, providerPaymentId, "authorised");
            await Task.Delay(TimeSpan.FromSeconds(15), ct); // longer than our 10 s timeout
        }

        return new ProviderAuthResult(!declined, providerPaymentId, declined ? "card_declined" : null);
    }

    public async Task<ProviderLookupResult> LookupAsync(string idempotencyKey, CancellationToken ct)
    {
        await SimulateTroubleAsync(ct, slowdown: false);
        var found = await FindAsync(idempotencyKey, ct);
        if (found is null) return new ProviderLookupResult(ProviderStatus.NotFound, null);
        return new ProviderLookupResult(Parse(found.Value.Status), found.Value.ProviderPaymentId);
    }

    public async Task CaptureAsync(string providerPaymentId, string idempotencyKey, CancellationToken ct)
    {
        await SimulateTroubleAsync(ct);
        await SetStatusAsync(providerPaymentId, "captured", ct); // setting the same status twice is harmless (idempotent)
    }

    public async Task VoidAsync(string providerPaymentId, string idempotencyKey, CancellationToken ct)
    {
        await SimulateTroubleAsync(ct);
        await SetStatusAsync(providerPaymentId, "voided", ct);
    }

    public async Task<IReadOnlyList<SettlementLine>> GetSettlementReportAsync(DateOnly day, CancellationToken ct)
    {
        await using var cmd = _db.CreateCommand(
            "SELECT reference, provider_payment_id, status, amount_minor FROM fake_provider.charges WHERE created_at::date = @day");
        cmd.Parameters.AddWithValue("day", day);
        var lines = new List<SettlementLine>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lines.Add(new SettlementLine(r.GetString(0), r.GetString(1), Parse(r.GetString(2)), r.GetInt64(3)));
        return lines;
    }

    // ---- simulation helpers ----------------------------------------------------------------
    private async Task SimulateTroubleAsync(CancellationToken ct, bool slowdown = true)
    {
        if (await flags.IsEnabledAsync("fake-provider-down", ct))
            throw new ProviderUnavailableException("503 Service Unavailable (simulated outage)");
        if (slowdown && await flags.IsEnabledAsync("fake-provider-slow", ct))
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
    }

    /// <summary>Real providers call us back. The fake does too, a few seconds later, signed with a shared secret.</summary>
    private async Task SendWebhookLaterAsync(string reference, string providerPaymentId, string status)
    {
        var url = configuration["Payments:Fake:WebhookUrl"];
        if (string.IsNullOrEmpty(url)) return;
        await Task.Delay(TimeSpan.FromSeconds(12)); // arrives AFTER our 10 s timeout gave up - like real life
        var body = JsonSerializer.Serialize(new { id = "evt_" + Guid.NewGuid().ToString("N")[..12], reference, providerPaymentId, status });
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(configuration["Payments:WebhookSecret"] ?? ""), Encoding.UTF8.GetBytes(body)));
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Provider-Signature", signature);
        try { await http.CreateClient().SendAsync(request); logger.LogInformation("FAKE PROVIDER: webhook sent for {Reference}", reference); }
        catch (Exception ex) { logger.LogWarning("FAKE PROVIDER: webhook delivery failed ({Error}) - the sweeper will still find it", ex.Message); }
    }

    private static ProviderStatus Parse(string s) => s switch
    {
        "authorised" => ProviderStatus.Authorised, "declined" => ProviderStatus.Declined,
        "captured" => ProviderStatus.Captured, "voided" => ProviderStatus.Voided, _ => ProviderStatus.NotFound,
    };

    private async Task<(string ProviderPaymentId, string Status)?> FindAsync(string key, CancellationToken ct)
    {
        await using var cmd = _db.CreateCommand("SELECT provider_payment_id, status FROM fake_provider.charges WHERE idempotency_key = @k");
        cmd.Parameters.AddWithValue("k", key);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? (r.GetString(0), r.GetString(1)) : null;
    }

    private async Task InsertAsync(AuthoriseRequest req, string providerPaymentId, string status, CancellationToken ct)
    {
        await using var cmd = _db.CreateCommand("""
            INSERT INTO fake_provider.charges (idempotency_key, provider_payment_id, reference, amount_minor, status, created_at)
            VALUES (@k, @p, @r, @a, @s, now()) ON CONFLICT (idempotency_key) DO NOTHING
            """);
        cmd.Parameters.AddWithValue("k", req.IdempotencyKey);
        cmd.Parameters.AddWithValue("p", providerPaymentId);
        cmd.Parameters.AddWithValue("r", req.Reference);
        cmd.Parameters.AddWithValue("a", req.Amount.ToMinorUnits());
        cmd.Parameters.AddWithValue("s", status);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task SetStatusAsync(string providerPaymentId, string status, CancellationToken ct)
    {
        await using var cmd = _db.CreateCommand("UPDATE fake_provider.charges SET status = @s WHERE provider_payment_id = @p");
        cmd.Parameters.AddWithValue("s", status);
        cmd.Parameters.AddWithValue("p", providerPaymentId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
