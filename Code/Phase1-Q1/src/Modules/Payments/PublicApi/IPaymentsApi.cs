using FoodDelivery.BuildingBlocks;

// ============================================================================================
// PUBLIC API of the Payments module.
//
// D6 - AUTHORISE AT CHECKOUT, CAPTURE ON ACCEPT, VOID ON REJECT
//   Authorise = put a HOLD on the customer's card (no money moves yet)
//   Capture   = actually TAKE the held money (when the restaurant accepts)
//   Void      = RELEASE the hold (when the restaurant rejects) - free, instant, no refund needed
//
// Payments is the ONLY module that knows a payment provider (Stripe) exists. Ordering just calls
// these three methods. Swap Stripe for another provider and Ordering never changes.
// ============================================================================================
namespace FoodDelivery.Modules.Payments.PublicApi;

public interface IPaymentsApi
{
    /// <param name="paymentToken">
    /// A token from the provider's hosted card fields. The card NUMBER never reaches our servers
    /// (keeps us out of most PCI compliance work).
    /// </param>
    Task<PaymentResult> AuthoriseAsync(Guid orderId, Money amount, string paymentToken, CancellationToken ct);

    Task<PaymentResult> CaptureAsync(Guid orderId, CancellationToken ct);

    Task<PaymentResult> VoidAsync(Guid orderId, CancellationToken ct);
}

public enum PaymentOutcome
{
    Succeeded,
    Declined, // the provider gave a definite "no" (e.g. insufficient funds)
    Failed,   // Q1: errors AND TIMEOUTS land here. See the warning in Features/Authorise.
}

public sealed record PaymentResult(PaymentOutcome Outcome, string? Reason = null)
{
    public bool IsSuccess => Outcome == PaymentOutcome.Succeeded;
}
