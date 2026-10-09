using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.PartnerIntegration.Data;
using FoodDelivery.Modules.PartnerIntegration.Translation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FoodDelivery.Modules.PartnerIntegration.Features.PartnerStatus;

// ============================================================================================
// CHALLENGE FLOW 4.2 (continued) - status updates coming BACK from their platform.
//   their restaurant accepts / cooks / dispatches / delivers in THEIR tools
//   -> their webhook -> our INBOX (dedupe) -> TRANSLATE -> Ordering's PUBLIC API
// From there our existing flow runs: accept -> capture, reject -> void, delivered -> notify.
// ============================================================================================

/// <summary>SLICE: receive their webhook - verify, store in the inbox, reply fast.</summary>
internal static class ReceivePartnerWebhook
{
    private sealed record Payload(
        [property: JsonPropertyName("event_id")] string EventId,
        [property: JsonPropertyName("order_no")] string OrderNo,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("reason")] string? Reason);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/integration/partner/webhooks", Handle).AllowAnonymous().WithTags("Integration");

    private static async Task<IResult> Handle(HttpRequest request, IntegrationStore store, IConfiguration configuration, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(configuration["Partner:WebhookSecret"] ?? ""), Encoding.UTF8.GetBytes(body)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(request.Headers["X-Partner-Signature"].ToString())))
            return Results.Unauthorized();

        var payload = JsonSerializer.Deserialize<Payload>(body)!;
        await using var cmd = store.DataSource.CreateCommand("""
            INSERT INTO partner_integration.partner_inbox (event_id, their_order_no, their_status, reason, received_at)
            VALUES (@id, @no, @s, @r, now()) ON CONFLICT (event_id) DO NOTHING
            """);
        cmd.Parameters.AddWithValue("id", payload.EventId);
        cmd.Parameters.AddWithValue("no", payload.OrderNo);
        cmd.Parameters.AddWithValue("s", payload.Status);
        cmd.Parameters.AddWithValue("r", (object?)payload.Reason ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
        return Results.Ok();
    }
}

/// <summary>
/// WORKER: apply inbox entries. Translates THEIR status to OUR update and calls Ordering's public API.
/// An unknown status is NOT guessed: the entry is parked with an error (an alert would fire).
/// </summary>
internal sealed class PartnerInboxProcessor(IServiceProvider services, ILogger<PartnerInboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IntegrationStore>();
                var ordering = scope.ServiceProvider.GetRequiredService<IOrderingApi>();

                var pending = new List<(string EventId, string OrderNo, string Status, string? Reason, Guid? OurOrderId)>();
                await using (var cmd = store.DataSource.CreateCommand("""
                    SELECT i.event_id, i.their_order_no, i.their_status, i.reason, f.our_order_id
                    FROM partner_integration.partner_inbox i
                    LEFT JOIN partner_integration.forwarded_orders f ON f.their_order_no = i.their_order_no
                    WHERE i.processed_at IS NULL AND i.error IS NULL ORDER BY i.received_at LIMIT 50
                    """))
                await using (var r = await cmd.ExecuteReaderAsync(stop))
                    while (await r.ReadAsync(stop))
                        pending.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetGuid(4)));

                foreach (var entry in pending)
                {
                    string? error = null;
                    try
                    {
                        var update = PartnerTranslator.ToOurUpdate(entry.Status);
                        if (entry.OurOrderId is { } orderId)
                        {
                            var applied = await ordering.ApplyPartnerUpdateAsync(orderId, update, entry.Reason, stop);
                            logger.LogInformation("Partner {Their} '{Status}' -> our {Update} on order {OrderId}: {Result}",
                                entry.OrderNo, entry.Status, update, orderId, applied ? "applied" : "ignored (repeat or backwards)");
                            if (update == PartnerOrderUpdate.Accepted) await MarkConfirmedAsync(store, orderId, stop);
                        }
                        else error = "unknown partner order number";
                    }
                    catch (UnknownPartnerStatusException ex) { error = ex.Message; logger.LogError("ALERT: {Error}", ex.Message); }

                    await using var done = store.DataSource.CreateCommand(
                        "UPDATE partner_integration.partner_inbox SET processed_at = CASE WHEN @e IS NULL THEN now() END, error = @e WHERE event_id = @id");
                    done.Parameters.Add(new NpgsqlParameter("e", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)error ?? DBNull.Value });
                    done.Parameters.AddWithValue("id", entry.EventId);
                    await done.ExecuteNonQueryAsync(stop);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Partner inbox round failed."); }
            await Task.Delay(TimeSpan.FromSeconds(2), stop);
        }
    }

    private static async Task MarkConfirmedAsync(IntegrationStore store, Guid orderId, CancellationToken ct)
    {
        await using var cmd = store.DataSource.CreateCommand("UPDATE partner_integration.forwarded_orders SET confirmed_at = coalesce(confirmed_at, now()) WHERE our_order_id = @id");
        cmd.Parameters.AddWithValue("id", orderId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// WORKER: the 10-minute rule. A partner order nobody confirmed is REJECTED through Ordering's
/// public API - which publishes OrderRejected - which makes Payments VOID the hold. Reused, not rebuilt.
/// </summary>
internal sealed class UnconfirmedPartnerOrderWatchdog(IServiceProvider services, IConfiguration configuration, ILogger<UnconfirmedPartnerOrderWatchdog> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var window = TimeSpan.FromMinutes(configuration.GetValue("Partner:ConfirmWithinMinutes", 10.0));
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IntegrationStore>();
                var ordering = scope.ServiceProvider.GetRequiredService<IOrderingApi>();
                var late = new List<Guid>();
                await using (var cmd = store.DataSource.CreateCommand("""
                    SELECT our_order_id FROM partner_integration.forwarded_orders
                    WHERE confirmed_at IS NULL AND NOT timed_out AND forwarded_at < now() - @window
                    """))
                {
                    cmd.Parameters.AddWithValue("window", window);
                    await using var r = await cmd.ExecuteReaderAsync(stop);
                    while (await r.ReadAsync(stop)) late.Add(r.GetGuid(0));
                }
                foreach (var orderId in late)
                {
                    await ordering.ApplyPartnerUpdateAsync(orderId, PartnerOrderUpdate.Rejected, "partner did not confirm in time", stop);
                    await using var mark = store.DataSource.CreateCommand("UPDATE partner_integration.forwarded_orders SET timed_out = true WHERE our_order_id = @id");
                    mark.Parameters.AddWithValue("id", orderId);
                    await mark.ExecuteNonQueryAsync(stop);
                    logger.LogWarning("Partner never confirmed order {OrderId} - rejected; payment hold will be released.", orderId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Watchdog round failed."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stop);
        }
    }
}
