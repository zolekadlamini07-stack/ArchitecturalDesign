using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.Data;
using FoodDelivery.Modules.Ordering.Domain;
using FoodDelivery.Modules.Ordering.PublicApi;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Modules.Ordering.Features.PartnerUpdates;

/// <summary>
/// CHALLENGE SLICE: apply a (translated) partner status update to one of OUR orders.
/// It goes through the same Order state machine and publishes the same events as our own
/// restaurants' slices - so capture, void and notifications work for partner orders unchanged.
/// </summary>
internal sealed class ApplyPartnerUpdate(OrderingDbContext db) : IOrderingApi
{
    public async Task<bool> ApplyPartnerUpdateAsync(Guid orderId, PartnerOrderUpdate update, string? reason, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.Fulfilment == Fulfilment.Partner, ct);
        if (order is null) return false;

        try
        {
            switch (update)
            {
                case PartnerOrderUpdate.Accepted:
                    order.Accept();
                    db.AddToOutbox(new OrderAccepted(order.Id, order.CustomerId, order.RestaurantId)); // -> capture
                    break;
                case PartnerOrderUpdate.Rejected:
                    order.Reject(reason ?? "rejected by partner");
                    db.AddToOutbox(new OrderRejected(order.Id, order.CustomerId, order.RestaurantId, reason ?? "rejected by partner")); // -> void
                    break;
                case PartnerOrderUpdate.Preparing: order.StartPreparing(); break;
                case PartnerOrderUpdate.OutForDelivery:
                    if (order.Status == OrderStatus.Preparing) order.MarkReadyForPickup(); // partner skips "ready"; we pass through it
                    order.MarkOutForDelivery();
                    break;
                case PartnerOrderUpdate.Delivered:
                    order.MarkDelivered();
                    db.AddToOutbox(new OrderDelivered(order.Id, order.CustomerId, order.RestaurantId));
                    break;
            }
        }
        catch (OrderRuleException)
        {
            return false; // repeated or backwards update: ignore, never corrupt the order
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<OrderForForwarding?> GetOrderForForwardingAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId && o.Fulfilment == Fulfilment.Partner, ct);
        return order is null ? null : new OrderForForwarding(order.Id, order.RestaurantId, order.DeliveryAddress, order.Total, order.Currency,
            order.Lines.Select(l => new OrderLineForForwarding(l.MenuItemId, l.Quantity)).ToList());
    }
}
