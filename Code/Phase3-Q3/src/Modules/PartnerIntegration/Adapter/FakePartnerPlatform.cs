using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoodDelivery.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FoodDelivery.Modules.PartnerIntegration.Adapter;

/// <summary>
/// DEVELOPMENT ONLY - a stand-in for the ACQUIRED COMPANY'S platform, speaking THEIR model.
/// Switch it "down" with the feature flag fake-partner-down to see the circuit breaker and the
/// "restaurants temporarily unavailable" behaviour.
/// </summary>
internal sealed class FakePartnerPlatform(IConfiguration configuration, IFeatureFlags flags) : IPartnerPlatform
{
    private readonly NpgsqlDataSource _db = NpgsqlDataSource.Create(configuration.ConnectionStringFor("Default"));

    public async Task<IReadOnlyList<TheirStore>> GetStoresAsync(CancellationToken ct)
    {
        await ThrowIfDownAsync(ct);
        return
        [
            new TheirStore(8812, "Bob's Burgers (acquired)", "3 Kloof St", "Cape Town", true, 1500,
            [
                new TheirSection("Mains", [new TheirProduct("BB-01", "Classic Burger", 7500, 1), new TheirProduct("BB-02", "Veggie Burger", 7000, null)]),
                new TheirSection("Sides", [new TheirProduct("BB-10", "Chips", 2500, 1)]),
            ]),
        ];
    }

    public async Task<TheirOrderCreated> CreateOrderAsync(TheirOrderRequest request, CancellationToken ct)
    {
        await ThrowIfDownAsync(ct);
        // Idempotent on external_ref, like a well-behaved partner API.
        await using var cmd = _db.CreateCommand("""
            INSERT INTO fake_partner.orders (order_no, external_ref, store_id, status, created_at)
            VALUES (@no, @ref, @store, 'NEW', now())
            ON CONFLICT (external_ref) DO UPDATE SET external_ref = excluded.external_ref
            RETURNING order_no, status
            """);
        cmd.Parameters.AddWithValue("no", "B-" + Random.Shared.Next(1000, 9999));
        cmd.Parameters.AddWithValue("ref", request.ExternalRef);
        cmd.Parameters.AddWithValue("store", request.StoreId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return new TheirOrderCreated(r.GetString(0), r.GetString(1));
    }

    private async Task ThrowIfDownAsync(CancellationToken ct)
    {
        if (await flags.IsEnabledAsync("fake-partner-down", ct)) throw new PartnerUnavailableException("partner API unavailable (simulated)");
    }

    /// <summary>
    /// DEV endpoint that plays the PARTNER'S RESTAURANT: "change my order's status and send the
    /// webhook". It signs and POSTs a real webhook to our ReceivePartnerWebhook endpoint.
    ///   POST /dev/partner/orders/{theirOrderNo}/status?status=CONFIRMED|COOKING|DISPATCHED|DONE|CANCELLED
    /// </summary>
    public static void MapSimulator(IEndpointRouteBuilder app) =>
        app.MapPost("/dev/partner/orders/{orderNo}/status", async (string orderNo, string status, IConfiguration config, IHttpClientFactory http, CancellationToken ct) =>
        {
            await using (var db = NpgsqlDataSource.Create(config.ConnectionStringFor("Default")))
            await using (var cmd = db.CreateCommand("UPDATE fake_partner.orders SET status = @s WHERE order_no = @no"))
            {
                cmd.Parameters.AddWithValue("s", status);
                cmd.Parameters.AddWithValue("no", orderNo);
                if (await cmd.ExecuteNonQueryAsync(ct) == 0) return Results.NotFound();
            }
            var body = JsonSerializer.Serialize(new { event_id = "pevt_" + Guid.NewGuid().ToString("N")[..10], order_no = orderNo, status });
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(config["Partner:WebhookSecret"] ?? ""), Encoding.UTF8.GetBytes(body)));
            using var request = new HttpRequestMessage(HttpMethod.Post, config["Partner:Fake:WebhookUrl"]) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Partner-Signature", signature);
            var response = await http.CreateClient().SendAsync(request, ct);
            return Results.Ok(new { orderNo, status, webhookDelivered = response.IsSuccessStatusCode });
        }).AllowAnonymous().WithTags("Dev: partner simulator");
}
