using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoodDelivery.Modules.Payments.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FoodDelivery.Modules.Payments.Features.Reconciliation;

/// <summary>
/// Q3 SLICE - RECONCILIATION LAYER 1: WEBHOOKS ("the provider calls US back").
///
///   1. verify the signature (anyone on the internet can POST to this URL)
///   2. store the webhook in the INBOX, keyed by the provider's event id  -> duplicates are dropped
///   3. reply 200 FAST. Processing happens in the Worker (ReconciliationSweeper).
///
/// Webhooks are fast but NOT guaranteed (they can be late, lost, or arrive while we're down),
/// which is why the sweeper and the daily settlement check exist too. Never trust one channel
/// for the truth about money.
/// (Real Stripe signs with a "Stripe-Signature" header and its own scheme; the idea is identical.)
/// </summary>
internal static class ReceiveProviderWebhook
{
    private sealed record Payload(string Id, string Reference, string ProviderPaymentId, string Status);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/payments/webhooks/provider", Handle).AllowAnonymous().WithTags("Payments");

    private static async Task<IResult> Handle(HttpRequest request, PaymentsDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(ct);

        var expected = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(configuration["Payments:WebhookSecret"] ?? ""), Encoding.UTF8.GetBytes(body)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(request.Headers["X-Provider-Signature"].ToString())))
            return Results.Unauthorized();

        var payload = JsonSerializer.Deserialize<Payload>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        // INBOX: INSERT ... ON CONFLICT DO NOTHING - the same webhook twice is stored once.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payments.provider_webhook_inbox (provider_event_id, reference, status, received_at)
            VALUES ({payload.Id}, {payload.Reference}, {payload.Status}, now())
            ON CONFLICT (provider_event_id) DO NOTHING
            """, ct);
        return Results.Ok();
    }
}
