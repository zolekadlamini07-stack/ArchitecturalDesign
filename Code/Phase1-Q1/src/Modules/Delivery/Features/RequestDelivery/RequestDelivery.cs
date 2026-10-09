using FoodDelivery.Modules.Delivery.Data;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Delivery.Features.RequestDelivery;

/// <summary>
/// SLICE: "A restaurant's order is ready - create a delivery request".
/// Reached through the public API (IDeliveryApi). Ordering calls it when the restaurant marks an
/// order READY_FOR_PICKUP. The request then shows up for the restaurant's on-shift drivers.
/// </summary>
internal sealed class RequestDelivery(DeliveryDbContext db)
{
    public async Task HandleAsync(Guid orderId, Guid restaurantId, string deliveryAddress, CancellationToken ct)
    {
        // One delivery per order (UNIQUE order_id). If asked twice, do nothing the second time.
        if (await db.Deliveries.AnyAsync(d => d.OrderId == orderId, ct)) return;

        db.Deliveries.Add(new DeliveryJob
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            RestaurantId = restaurantId,
            Status = DeliveryStatus.Requested,
            DeliveryAddress = deliveryAddress,
            RequestedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        // Drivers see it the next time their app polls ListDeliveryRequests (D8: polling).
    }
}
