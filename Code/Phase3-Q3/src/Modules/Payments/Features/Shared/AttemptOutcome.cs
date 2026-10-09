using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Payments.Data;
using FoodDelivery.Modules.Payments.PublicApi;

namespace FoodDelivery.Modules.Payments.Features.Shared;

/// <summary>
/// The ONE place where an attempt's final outcome is recorded - shared by every slice that can
/// learn the truth: the authorise job, the webhook processor and the reconciliation sweeper.
/// (Same "pragmatic VSA" idea as the Order aggregate: one rule, one home.)
///
/// IDEMPOTENT: they may all learn the same fact, in any order, any number of times. Only the
/// FIRST one changes anything; the rest are no-ops. So a late webhook after the sweeper already
/// resolved the attempt does nothing.
///
/// The status change, the audit line and the outbox event are saved in ONE transaction by the
/// caller's SaveChanges - so "authorised" can never be recorded without PaymentAuthorised being
/// published (Q3 walkthrough 5.5: crash after payment succeeds).
/// </summary>
internal static class AttemptOutcome
{
    private static readonly string[] Open = [AttemptStatus.Requested, AttemptStatus.InFlight, AttemptStatus.Unknown];

    public static bool Authorised(PaymentsDbContext db, PaymentAttempt attempt, string? providerPaymentId, string how)
    {
        if (!Open.Contains(attempt.Status)) return false; // already decided - nothing to do
        attempt.Status = AttemptStatus.Authorised;
        attempt.ProviderPaymentId = providerPaymentId;
        attempt.UpdatedAt = DateTimeOffset.UtcNow;
        db.Record(attempt, "AUTHORISED", how);
        db.AddToOutbox(new PaymentAuthorised(attempt.OrderId, attempt.Id));
        return true;
    }

    public static bool Declined(PaymentsDbContext db, PaymentAttempt attempt, string reason, string how)
    {
        if (!Open.Contains(attempt.Status)) return false;
        attempt.Status = AttemptStatus.Declined;
        attempt.UpdatedAt = DateTimeOffset.UtcNow;
        db.Record(attempt, "DECLINED", $"{reason} ({how})");
        db.AddToOutbox(new PaymentDeclined(attempt.OrderId, attempt.Id, reason));
        return true;
    }
}
