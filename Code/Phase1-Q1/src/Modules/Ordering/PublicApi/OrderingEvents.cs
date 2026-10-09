using FoodDelivery.BuildingBlocks;

// ============================================================================================
// PUBLIC API of the Ordering module (events).
// Facts about orders that other modules may react to. In Q1 nobody subscribes yet - the slices
// call Notifications directly. In Q2 these become DURABLE events (outbox) and Notifications
// subscribes to them instead, which is what removes notification latency from the request.
// ============================================================================================
namespace FoodDelivery.Modules.Ordering.PublicApi;

public sealed record OrderPlaced(Guid OrderId, Guid CustomerId, Guid RestaurantId, decimal Total, string Currency) : DomainEvent;
public sealed record OrderAccepted(Guid OrderId, Guid CustomerId, Guid RestaurantId) : DomainEvent;
public sealed record OrderRejected(Guid OrderId, Guid CustomerId, Guid RestaurantId, string Reason) : DomainEvent;
public sealed record OrderReadyForPickup(Guid OrderId, Guid RestaurantId) : DomainEvent;
public sealed record OrderDelivered(Guid OrderId, Guid CustomerId, Guid RestaurantId) : DomainEvent;
