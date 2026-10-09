using FoodDelivery.BuildingBlocks;

// ============================================================================================
// PUBLIC API of the Payments module (Q3).
//
// Q1/Q2: Ordering CALLED Payments synchronously: AuthoriseAsync / CaptureAsync / VoidAsync.
// Q3:    nobody calls Payments to move money any more. Ordering publishes facts (OrderPlaced,
//        OrderAccepted...) and Payments reacts IN THE WORKER, behind a circuit breaker. Payments
//        answers with these events. The only thing left to CALL is a read-only support view.
// ============================================================================================
namespace FoodDelivery.Modules.Payments.PublicApi;

/// <summary>The provider said YES: money is held for this order. Ordering may show it to the restaurant.</summary>
public sealed record PaymentAuthorised(Guid OrderId, Guid AttemptId) : DomainEvent;

/// <summary>A DEFINITE no (declined card, or reconciliation proved nothing was ever charged).</summary>
public sealed record PaymentDeclined(Guid OrderId, Guid AttemptId, string Reason) : DomainEvent;

public interface IPaymentsApi
{
    /// <summary>
    /// Everything Payments knows about an order's money, oldest first - from the append-only
    /// payment_events log. Used by the support "order timeline" (Q3 change C8: "how do we know
    /// what happened after the fact?").
    /// </summary>
    Task<IReadOnlyList<PaymentTimelineEntry>> GetTimelineAsync(Guid orderId, CancellationToken ct);
}

public sealed record PaymentTimelineEntry(DateTimeOffset At, Guid AttemptId, string What, string? Detail);
