using FoodDelivery.BuildingBlocks;

// ============================================================================================
// PUBLIC API of the Delivery module.
//
//   COMMAND (a call):  Ordering -> IDeliveryApi.RequestDeliveryAsync  "please find a driver"
//   FACTS   (events):  Delivery -> DeliveryPickedUp / DeliveryCompleted  "this happened"
//
// Why both? Ordering must TELL Delivery to start (a command). But if Delivery then CALLED
// Ordering back, the two modules would depend on each other in a circle. Instead Delivery only
// ANNOUNCES what happened, and Ordering subscribes (D5). Delivery never references Ordering.
// ============================================================================================
namespace FoodDelivery.Modules.Delivery.PublicApi;

public interface IDeliveryApi
{
    /// <summary>
    /// Creates a delivery request visible to the restaurant's ON-SHIFT drivers.
    /// D7: drivers belong to restaurants, so only THIS restaurant's drivers ever see it.
    /// </summary>
    Task RequestDeliveryAsync(Guid orderId, Guid restaurantId, string deliveryAddress, CancellationToken ct);
}

/// <summary>A driver has collected the food. Ordering moves the order to OUT_FOR_DELIVERY.</summary>
public sealed record DeliveryPickedUp(Guid OrderId, Guid DriverId) : DomainEvent;

/// <summary>The food has been handed over. Ordering moves the order to DELIVERED.</summary>
public sealed record DeliveryCompleted(Guid OrderId, Guid DriverId) : DomainEvent;
