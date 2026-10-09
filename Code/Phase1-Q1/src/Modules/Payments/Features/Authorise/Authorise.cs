using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Payments.Data;
using FoodDelivery.Modules.Payments.Provider;
using FoodDelivery.Modules.Payments.PublicApi;
using Microsoft.Extensions.Logging;

namespace FoodDelivery.Modules.Payments.Features.Authorise;

// ============================================================================================
// SLICE: "Authorise (hold) the money for an order".
// Not an HTTP endpoint - it is reached through the module's PUBLIC API (IPaymentsApi), which
// Ordering calls in-process while placing an order. The public API simply delegates to this slice.
// ============================================================================================
internal sealed class Authorise(PaymentsDbContext db, IPaymentProvider provider, ILogger<Authorise> logger)
{
    /// <summary>How long we wait for the provider before giving up.</summary>
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(10);

    public async Task<PaymentResult> HandleAsync(Guid orderId, Money amount, string paymentToken, CancellationToken ct)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(), OrderId = orderId, Amount = amount.Amount, Currency = amount.Currency,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProviderTimeout);

            var result = await provider.AuthoriseAsync(new AuthoriseRequest(orderId.ToString(), amount, paymentToken), timeout.Token);

            payment.Status = result.Authorised ? PaymentStatus.Authorised : PaymentStatus.Declined;
            payment.ProviderPaymentId = result.ProviderPaymentId;
            payment.FailureReason = result.DeclineReason;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            // ====================================================================================
            // Q1 KNOWN WEAKNESS - RISK R2 (accepted deliberately at 1-restaurant scale)
            //
            // A TIMEOUT IS TREATED AS "FAILED". But a timeout really means "we don't know": the
            // provider may have authorised the card and only the reply was lost. The customer is
            // told "payment failed", tries again, and may end up with TWO holds on their card.
            //
            // At Q1 scale staff fix this by hand in the provider's dashboard. On the "Friday night"
            // in Q3 this exact line causes the incident, and is replaced by an explicit UNKNOWN
            // state + idempotency keys + reconciliation.
            // ====================================================================================
            logger.LogWarning(ex, "Payment provider did not answer for order {OrderId}; recording FAILED (Q1 behaviour).", orderId);
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = "provider_unavailable";
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        return payment.Status switch
        {
            PaymentStatus.Authorised => new PaymentResult(PaymentOutcome.Succeeded),
            PaymentStatus.Declined => new PaymentResult(PaymentOutcome.Declined, payment.FailureReason),
            _ => new PaymentResult(PaymentOutcome.Failed, payment.FailureReason),
        };
    }
}
