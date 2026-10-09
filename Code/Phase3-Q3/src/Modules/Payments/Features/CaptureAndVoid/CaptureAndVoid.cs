using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.Payments.Data;
using FoodDelivery.Modules.Payments.Provider;
using Microsoft.EntityFrameworkCore;
using Polly.CircuitBreaker;

namespace FoodDelivery.Modules.Payments.Features.CaptureAndVoid;

// ============================================================================================
// Q3: capture and void are JOBS reacting to Ordering's events, not calls inside a request.
// The restaurant presses Accept and gets an instant answer; the capture happens in the background
// and is retried until it works. The restaurant can keep cooking meanwhile: an authorisation holds
// the money for about 7 days (Q3 walkthrough 5.5, last row).
// Each call uses its OWN idempotency key ("capture-{attempt}", "void-{attempt}").
// ============================================================================================

/// <summary>SLICE: restaurant accepted -> CAPTURE the held money.</summary>
[JobQueue(JobQueues.Payments)]
internal sealed class CaptureOnOrderAccepted(PaymentsDbContext db, IPaymentProvider provider) : IDomainEventHandler<OrderAccepted>
{
    public async Task HandleAsync(OrderAccepted e, CancellationToken ct)
    {
        var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.OrderId == e.OrderId && a.Status != AttemptStatus.Declined, ct);
        if (attempt is not { Status: AttemptStatus.Authorised, ProviderPaymentId: not null }) return; // already captured, or nothing to capture

        try { await provider.CaptureAsync(attempt.ProviderPaymentId, $"capture-{attempt.Id}", ct); }
        catch (BrokenCircuitException) { throw new RetryLaterException(TimeSpan.FromSeconds(30), "circuit open - capture postponed"); }
        // any other error: let it throw -> the job is retried with backoff, then dead-lettered + alerted

        attempt.Status = AttemptStatus.Captured;
        attempt.UpdatedAt = DateTimeOffset.UtcNow;
        db.Record(attempt, "CAPTURED", "restaurant accepted the order");
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// The shared "release the hold" step, used by two slices below (COMPENSATION - Q3 change C6).
/// Also handles attempts that were never sent: they are closed as VOIDED so a postponed authorise
/// job can never charge an order that has already been given up.
/// </summary>
internal sealed class ReleaseHold(PaymentsDbContext db, IPaymentProvider provider)
{
    public async Task ReleaseAsync(Guid orderId, string why, CancellationToken ct)
    {
        var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.OrderId == orderId && a.Status != AttemptStatus.Declined, ct);
        if (attempt is null) return;

        switch (attempt.Status)
        {
            case AttemptStatus.Requested: // never sent to the provider: just close it
                attempt.Status = AttemptStatus.Voided;
                db.Record(attempt, "VOIDED", $"{why} - request was never sent, nothing was held");
                break;

            case AttemptStatus.Authorised when attempt.ProviderPaymentId is not null:
                try { await provider.VoidAsync(attempt.ProviderPaymentId, $"void-{attempt.Id}", ct); }
                catch (BrokenCircuitException) { throw new RetryLaterException(TimeSpan.FromSeconds(30), "circuit open - void postponed"); }
                attempt.Status = AttemptStatus.Voided;
                db.Record(attempt, "VOIDED", $"{why} - hold released, customer never charged");
                break;

            default:
                // IN_FLIGHT / UNKNOWN: we don't know yet. Reconciliation will resolve it; if it turns
                // out AUTHORISED, Ordering answers with OrderPaymentAbandoned again and we void then.
                return;
        }
        attempt.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>SLICE: restaurant rejected -> VOID (free, instant, no refund needed).</summary>
[JobQueue(JobQueues.Payments)]
internal sealed class VoidOnOrderRejected(ReleaseHold release) : IDomainEventHandler<OrderRejected>
{
    public Task HandleAsync(OrderRejected e, CancellationToken ct) => release.ReleaseAsync(e.OrderId, "restaurant rejected", ct);
}

/// <summary>
/// SLICE: Ordering gave up waiting for this payment (or a late success arrived for an order that
/// already failed) -> release any hold. "No customer keeps a charge for food they didn't get."
/// </summary>
[JobQueue(JobQueues.Payments)]
internal sealed class VoidOnOrderPaymentAbandoned(ReleaseHold release) : IDomainEventHandler<OrderPaymentAbandoned>
{
    public Task HandleAsync(OrderPaymentAbandoned e, CancellationToken ct) => release.ReleaseAsync(e.OrderId, "order no longer needs payment", ct);
}
