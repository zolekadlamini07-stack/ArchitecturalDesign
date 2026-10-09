using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.Data;
using FoodDelivery.Modules.Ordering.Domain;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.Payments.PublicApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodDelivery.Modules.Ordering.Features.PaymentOutcome;

// ============================================================================================
// Q3: ORDERING REACTS TO PAYMENT FACTS (events from Payments, run as jobs in the Worker).
// Payments never decides what happens to the ORDER, and Ordering never decides how to talk to the
// PROVIDER. They agree through events.
// ============================================================================================

/// <summary>
/// SLICE: "Payment was authorised".
///   order still waiting      -> AWAITING_ACCEPTANCE, and only NOW is the restaurant told
///   order already given up   -> COMPENSATE: publish OrderPaymentAbandoned so Payments releases the hold
///                               (Q3 walkthrough 5.2: "late success for a given-up order -> void")
///   already past this point  -> nothing (a repeated event changes nothing - idempotent)
/// This is how "some restaurants receive orders, some don't" is fixed: the restaurant sees an order
/// if and only if its money is held.
/// </summary>
internal sealed class WhenPaymentAuthorised(OrderingDbContext db, ILogger<WhenPaymentAuthorised> logger) : IDomainEventHandler<PaymentAuthorised>
{
    public async Task HandleAsync(PaymentAuthorised e, CancellationToken ct)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == e.OrderId, ct);
        if (order.IsWaitingForPayment)
        {
            order.MarkPaymentAuthorised();
            db.AddToOutbox(new OrderAwaitingAcceptance(order.Id, order.CustomerId, order.RestaurantId, order.Fulfilment));
        }
        else if (order.NoLongerNeedsPayment)
        {
            logger.LogWarning("LATE payment success for order {OrderId} which is {Status} - releasing the hold.", order.Id, order.Status);
            db.AddToOutbox(new OrderPaymentAbandoned(order.Id));
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>SLICE: "Payment was declined" -> PAYMENT_FAILED, and the customer is told clearly.</summary>
internal sealed class WhenPaymentDeclined(OrderingDbContext db) : IDomainEventHandler<PaymentDeclined>
{
    public async Task HandleAsync(PaymentDeclined e, CancellationToken ct)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == e.OrderId, ct);
        if (!order.IsWaitingForPayment) return; // already decided
        order.MarkPaymentFailed(e.Reason);
        db.AddToOutbox(new OrderPaymentFailed(order.Id, order.CustomerId, e.Reason));
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// WORKER JOB: "We've waited long enough" (default 10 minutes, Ordering:PaymentConfirmationWindowMinutes).
/// A customer shouldn't stare at "Confirming your payment..." for an hour while the provider is down,
/// and a restaurant must never get a 40-minute-old order. So after the window:
///   order -> PAYMENT_FAILED ("we couldn't confirm your payment; you have not been charged")
///   + OrderPaymentAbandoned -> Payments closes any unsent attempt, and voids any hold that turns up later.
/// </summary>
internal sealed class ExpireUnconfirmedPayments(IServiceProvider services, IConfiguration configuration, ILogger<ExpireUnconfirmedPayments> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var window = TimeSpan.FromMinutes(configuration.GetValue("Ordering:PaymentConfirmationWindowMinutes", 10.0));
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
                var cutoff = DateTimeOffset.UtcNow - window;
                var expired = await db.Orders.Where(o => o.Status == OrderStatus.PaymentPending && o.CreatedAt < cutoff).Take(50).ToListAsync(stop);
                foreach (var order in expired)
                {
                    order.MarkPaymentFailed("payment_not_confirmed");
                    db.AddToOutbox(new OrderPaymentFailed(order.Id, order.CustomerId, "payment_not_confirmed"));
                    db.AddToOutbox(new OrderPaymentAbandoned(order.Id));
                    logger.LogWarning("Order {OrderId}: payment not confirmed within {Window} - giving up (any hold will be released).", order.Id, window);
                }
                await db.SaveChangesAsync(stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Expiry check failed."); }
            await Task.Delay(TimeSpan.FromSeconds(15), stop);
        }
    }
}
