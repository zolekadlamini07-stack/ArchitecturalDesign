using FoodDelivery.Modules.Payments.Data;
using FoodDelivery.Modules.Payments.Features.Shared;
using FoodDelivery.Modules.Payments.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace FoodDelivery.Modules.Payments.Features.Reconciliation;

// ============================================================================================
// Q3 - RECONCILIATION LAYER 2: THE SWEEPER ("we ring THEM to check"). Runs in the WORKER.
//
// Every 10 seconds (every minute in production):
//   a) apply webhooks waiting in the inbox
//   b) for every attempt stuck in UNKNOWN or IN_FLIGHT for more than 30 seconds, ask the provider
//      "what happened to the request with this idempotency key?"
//        authorised / captured -> AUTHORISED (+ PaymentAuthorised)   - "it succeeded after all"
//        declined              -> DECLINED   (+ PaymentDeclined)
//        not found             -> the request never arrived: RESEND IT WITH THE SAME KEY.
//                                 Safe by construction - the provider can't charge one key twice.
//
// This is what makes "the payment provider comes back online" (walkthrough 5.6) self-healing:
// while the breaker is OPEN the sweeper skips its turn; once it CLOSES, every UNKNOWN is resolved.
// Analogy: the night cleaner walking round every few minutes picking up anything left lying around.
// ============================================================================================
internal sealed class ReconciliationSweeper(IServiceProvider services, ILogger<ReconciliationSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
                var provider = scope.ServiceProvider.GetRequiredService<IPaymentProvider>();
                await ApplyWebhooksAsync(db, stop);
                await ResolveUnknownsAsync(db, provider, stop);
            }
            catch (BrokenCircuitException) { logger.LogInformation("Sweeper: circuit open, skipping this round."); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Sweeper round failed; next round will retry."); }

            await Task.Delay(TimeSpan.FromSeconds(10), stop);
        }
    }

    private async Task ApplyWebhooksAsync(PaymentsDbContext db, CancellationToken ct)
    {
        var pending = await db.WebhookInbox.Where(w => w.ProcessedAt == null).OrderBy(w => w.ReceivedAt).Take(50).ToListAsync(ct);
        foreach (var hook in pending)
        {
            if (Guid.TryParse(hook.Reference, out var attemptId) &&
                await db.Attempts.SingleOrDefaultAsync(a => a.Id == attemptId, ct) is { } attempt)
            {
                var changed = hook.Status == "authorised"
                    ? AttemptOutcome.Authorised(db, attempt, attempt.ProviderPaymentId, "learned from WEBHOOK")
                    : hook.Status == "declined" && AttemptOutcome.Declined(db, attempt, "declined", "learned from WEBHOOK");
                if (!changed) db.Record(attempt, "webhook ignored", $"'{hook.Status}' - attempt already {attempt.Status}");
            }
            hook.ProcessedAt = DateTimeOffset.UtcNow;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); } // resolved elsewhere first: fine
        }
    }

    private async Task ResolveUnknownsAsync(PaymentsDbContext db, IPaymentProvider provider, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-30);
        var stuck = await db.Attempts
            .Where(a => (a.Status == AttemptStatus.Unknown || a.Status == AttemptStatus.InFlight) && a.UpdatedAt < cutoff)
            .OrderBy(a => a.UpdatedAt).Take(20).ToListAsync(ct);

        foreach (var attempt in stuck)
        {
            var found = await provider.LookupAsync(attempt.Id.ToString(), ct);
            switch (found.Status)
            {
                case ProviderStatus.Authorised or ProviderStatus.Captured:
                    AttemptOutcome.Authorised(db, attempt, found.ProviderPaymentId, "learned by SWEEPER lookup - it succeeded despite the timeout");
                    break;
                case ProviderStatus.Declined:
                    AttemptOutcome.Declined(db, attempt, "declined", "learned by SWEEPER lookup");
                    break;
                case ProviderStatus.NotFound:
                    // Never arrived. Replay with the SAME key - cannot double-charge.
                    db.Record(attempt, "replay", "provider has no record of this key - resending with the SAME key");
                    var replay = await provider.AuthoriseAsync(
                        new AuthoriseRequest(attempt.Id.ToString(), attempt.Id.ToString(), attempt.Money, attempt.PaymentToken), ct);
                    if (replay.Authorised) AttemptOutcome.Authorised(db, attempt, replay.ProviderPaymentId, "SWEEPER replay");
                    else AttemptOutcome.Declined(db, attempt, replay.DeclineReason ?? "declined", "SWEEPER replay");
                    break;
            }
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); } // resolved elsewhere first: fine
        }
    }
}
