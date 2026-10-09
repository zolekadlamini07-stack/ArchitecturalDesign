using FoodDelivery.BuildingBlocks;

// ============================================================================================
// PUBLIC API of the Ordering module (events). Durable since Q2 (outbox).
//
// Q3 CHANGES:
//   OrderPlaced now STARTS the payment (Payments listens) and carries the payment TOKEN
//     (a token from the provider's hosted fields - never a card number).
//   OrderAwaitingAcceptance is NEW: the restaurant is told ONLY once payment is authorised.
//   OrderPaymentFailed is NEW: the customer is told clearly that no money will be taken.
//   OrderPaymentAbandoned is NEW: "we gave up waiting - if money is held anyway, release it".
// ============================================================================================
namespace FoodDelivery.Modules.Ordering.PublicApi;

public sealed record OrderPlaced(Guid OrderId, Guid CustomerId, Guid RestaurantId, decimal Total, string Currency, string PaymentToken) : DomainEvent;
public sealed record OrderAwaitingAcceptance(Guid OrderId, Guid CustomerId, Guid RestaurantId, string Fulfilment) : DomainEvent;
public sealed record OrderPaymentFailed(Guid OrderId, Guid CustomerId, string Reason) : DomainEvent;
public sealed record OrderPaymentAbandoned(Guid OrderId) : DomainEvent;
public sealed record OrderAccepted(Guid OrderId, Guid CustomerId, Guid RestaurantId) : DomainEvent;
public sealed record OrderRejected(Guid OrderId, Guid CustomerId, Guid RestaurantId, string Reason) : DomainEvent;
public sealed record OrderDelivered(Guid OrderId, Guid CustomerId, Guid RestaurantId) : DomainEvent;

/// <summary>How an order is fulfilled. PARTNER = the acquired company's restaurants and drivers (challenge).</summary>
public static class Fulfilment
{
    public const string Own = "OWN";
    public const string Partner = "PARTNER";
}
