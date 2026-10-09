using FoodDelivery.Modules.Payments.Data;
using FoodDelivery.Modules.Payments.Provider;
using FoodDelivery.Modules.Payments.PublicApi;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Payments.Features.CaptureAndVoid;

/// <summary>
/// SLICE: "Capture" - take the held money. Called when the RESTAURANT ACCEPTS the order.
/// </summary>
internal sealed class CapturePayment(PaymentsDbContext db, IPaymentProvider provider)
{
    public async Task<PaymentResult> HandleAsync(Guid orderId, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is not { Status: PaymentStatus.Authorised, ProviderPaymentId: not null })
            return new PaymentResult(PaymentOutcome.Failed, "No authorised payment to capture.");

        await provider.CaptureAsync(payment.ProviderPaymentId, ct);
        payment.Status = PaymentStatus.Captured;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new PaymentResult(PaymentOutcome.Succeeded);
    }
}

/// <summary>
/// SLICE: "Void" - release the hold. Called when the RESTAURANT REJECTS the order.
/// This is why we authorise-then-capture: a rejection costs nothing and needs no refund.
/// </summary>
internal sealed class VoidPayment(PaymentsDbContext db, IPaymentProvider provider)
{
    public async Task<PaymentResult> HandleAsync(Guid orderId, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is not { Status: PaymentStatus.Authorised, ProviderPaymentId: not null })
            return new PaymentResult(PaymentOutcome.Succeeded); // nothing held, nothing to release

        await provider.VoidAsync(payment.ProviderPaymentId, ct);
        payment.Status = PaymentStatus.Voided;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new PaymentResult(PaymentOutcome.Succeeded);
    }
}
