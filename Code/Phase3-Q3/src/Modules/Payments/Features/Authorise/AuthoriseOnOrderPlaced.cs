using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.Payments.Data;
using FoodDelivery.Modules.Payments.Features.Shared;
using FoodDelivery.Modules.Payments.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace FoodDelivery.Modules.Payments.Features.Authorise;

// ============================================================================================
// Q3 SLICE: "An order was placed - authorise its payment" (runs in the WORKER, 'payments' lane).
//
// Q1/Q2: PlaceOrder CALLED the provider inside the customer's HTTP request; a timeout = "failed".
// Q3:    PlaceOrder saves the order (PAYMENT_PENDING) + OrderPlaced in one transaction and returns
//        202. This job does the risky part, step by step:
//
//   1. find or create the ATTEMPT row (its id is the idempotency key)  - BEFORE calling anyone
//   2. mark it IN_FLIGHT and log "request sent"                         - BEFORE calling anyone
//   3. call the provider (10 s timeout + circuit breaker), sending the idempotency key
//   4. record exactly what we learned:
//        yes          -> AUTHORISED  + PaymentAuthorised (outbox)
//        no           -> DECLINED    + PaymentDeclined   (outbox)
//        no answer    -> UNKNOWN     (NOT failed!) - reconciliation will find out the truth
//        breaker open -> nothing was sent: back to REQUESTED, try again in 30 s
//
// WHAT IF THIS JOB RUNS TWICE? (at-least-once delivery, Q3 walkthrough 5.4)
//   - a unique index allows only ONE live attempt per order -> no second attempt is created
//   - if the attempt is already decided, we stop
//   - if it is IN_FLIGHT/UNKNOWN, we stop and let reconciliation resolve it - never fire a second
//     concurrent charge
//   - even if all that failed, the provider sees the SAME idempotency key and won't charge twice
// ============================================================================================
[JobQueue(JobQueues.Payments)]
internal sealed class AuthoriseOnOrderPlaced(PaymentsDbContext db, IPaymentProvider provider, ILogger<AuthoriseOnOrderPlaced> logger)
    : IDomainEventHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced e, CancellationToken ct)
    {
        // --- 1. find or create the attempt ------------------------------------------------------
        var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.OrderId == e.OrderId && a.Status != AttemptStatus.Declined, ct);
        if (attempt is null)
        {
            attempt = new PaymentAttempt
            {
                Id = Guid.NewGuid(), OrderId = e.OrderId, Amount = e.Total, Currency = e.Currency, PaymentToken = e.PaymentToken,
                Status = AttemptStatus.Requested, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Attempts.Add(attempt);
            db.Record(attempt, "attempt created", $"{attempt.Money} for order {e.OrderId}");
            await db.SaveChangesAsync(ct);
        }

        if (attempt.Status != AttemptStatus.Requested)
        {
            logger.LogInformation("Attempt {Attempt} is already {Status}; nothing to send.", attempt.Id, attempt.Status);
            return;
        }

        // --- 2. record that we are about to call (so a crash mid-call leaves a trace) ---------
        attempt.Status = AttemptStatus.InFlight;
        attempt.UpdatedAt = DateTimeOffset.UtcNow;
        db.Record(attempt, "request sent", $"idempotency key {attempt.Id}");
        await db.SaveChangesAsync(ct);

        // --- 3. call the provider ----------------------------------------------------------------
        ProviderAuthResult result;
        try
        {
            result = await provider.AuthoriseAsync(
                new AuthoriseRequest(IdempotencyKey: attempt.Id.ToString(), Reference: attempt.Id.ToString(), attempt.Money, attempt.PaymentToken), ct);
        }
        catch (BrokenCircuitException)
        {
            // The breaker refused to call: the request was NEVER SENT. Safe to try again later,
            // and it must not count as a failed attempt (RetryLater doesn't use up an attempt).
            attempt.Status = AttemptStatus.Requested;
            db.Record(attempt, "not sent", "circuit breaker open - provider is having problems");
            await db.SaveChangesAsync(ct);
            throw new RetryLaterException(TimeSpan.FromSeconds(30), "payment provider circuit is open");
        }
        catch (Exception ex) when (ex is TimeoutRejectedException or HttpRequestException or ProviderUnavailableException)
        {
            // ====================================================================================
            // THE LINE THAT CAUSED THE FRIDAY INCIDENT - now fixed.
            // Q1 wrote "FAILED" here. But we DON'T KNOW: the provider may have charged the card and
            // only the reply was lost. So: UNKNOWN. The customer keeps seeing "Confirming your
            // payment...", the restaurant sees nothing yet, and reconciliation finds the truth.
            // ====================================================================================
            attempt.Status = AttemptStatus.Unknown;
            attempt.UpdatedAt = DateTimeOffset.UtcNow;
            db.Record(attempt, "no answer", $"{ex.GetType().Name}: outcome UNKNOWN - reconciliation will resolve it");
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                // While we were waiting, a webhook or the sweeper already learned the real outcome.
                // Good - theirs is the truth. Do NOT overwrite it with "unknown".
                logger.LogInformation("Attempt {Attempt} was resolved by reconciliation while we waited.", attempt.Id);
            }
            logger.LogWarning("Payment for order {OrderId} is UNKNOWN ({Reason}).", e.OrderId, ex.GetType().Name);
            return;
        }

        // --- 4. record the definite answer (status + audit + outbox event in ONE transaction) --
        if (result.Authorised) AttemptOutcome.Authorised(db, attempt, result.ProviderPaymentId, "provider replied yes");
        else AttemptOutcome.Declined(db, attempt, result.DeclineReason ?? "declined", "provider replied no");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { /* already resolved elsewhere with the same truth - nothing to do */ }
    }
}
