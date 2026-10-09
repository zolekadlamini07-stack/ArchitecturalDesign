using FoodDelivery.BuildingBlocks;

// ============================================================================================
// PUBLIC API of the Ordering module (events).
// Facts about orders that other modules may react to.
// Q2: these are now DURABLE - written to ordering.outbox in the same transaction as the order, and
// delivered by the Worker. Notifications subscribes to them, which is what removed notification
// latency from PlaceOrder/AcceptOrder (problem P3).
// ============================================================================================
namespace FoodDelivery.Modules.Ordering.PublicApi;

public sealed record OrderPlaced(Guid OrderId, Guid CustomerId, Guid RestaurantId, decimal Total, string Currency) : DomainEvent;
public sealed record OrderAccepted(Guid OrderId, Guid CustomerId, Guid RestaurantId) : DomainEvent;
public sealed record OrderRejected(Guid OrderId, Guid CustomerId, Guid RestaurantId, string Reason) : DomainEvent;
public sealed record OrderReadyForPickup(Guid OrderId, Guid RestaurantId) : DomainEvent;
public sealed record OrderDelivered(Guid OrderId, Guid CustomerId, Guid RestaurantId) : DomainEvent;
