using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.PartnerIntegration.Adapter;
using FoodDelivery.Modules.PartnerIntegration.Data;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace FoodDelivery.Modules.PartnerIntegration.Features.ForwardOrder;

/// <summary>
/// CHALLENGE FLOW 4.2 - "A customer ordered from one of THEIR restaurants".
///
/// OUR PlaceOrder runs unchanged: our customer, OUR payment (Q3 flow). Once the payment is
/// authorised, Ordering publishes OrderAwaitingAcceptance (fulfilment = PARTNER). This job:
///   1. reads the order through Ordering's PUBLIC API (our ids only)
///   2. maps our ids to theirs (id_map) and builds THEIR order shape
///   3. sends it with external_ref = OUR order id  -> IDEMPOTENT: a retry can't create a duplicate
/// Runs in the Worker's 'integration' lane (a bulkhead: a slow partner can't block payments).
/// If they don't confirm within 10 minutes, the watchdog rejects it and our normal reject path
/// VOIDS the payment hold - no new failure logic, we reuse Q3's.
/// </summary>
[JobQueue(JobQueues.Integration)]
internal sealed class ForwardOrderToPartner(IOrderingApi ordering, IntegrationStore store, IPartnerPlatform partner, ILogger<ForwardOrderToPartner> logger)
    : IDomainEventHandler<OrderAwaitingAcceptance>
{
    public async Task HandleAsync(OrderAwaitingAcceptance e, CancellationToken ct)
    {
        if (e.Fulfilment != Fulfilment.Partner) return;            // our own restaurants: nothing to do
        if (await store.AlreadyForwardedAsync(e.OrderId, ct)) return; // job ran twice: already done

        var order = await ordering.GetOrderForForwardingAsync(e.OrderId, ct) ?? throw new InvalidOperationException($"Order {e.OrderId} not found.");
        var storeId = await store.TheirIdAsync("restaurant", order.RestaurantId, ct) ?? throw new InvalidOperationException("Restaurant is not mapped to a partner store.");

        var items = new List<TheirOrderItem>();
        foreach (var line in order.Lines)
            items.Add(new TheirOrderItem(await store.TheirIdAsync("menu_item", line.MenuItemId, ct) ?? throw new InvalidOperationException("Unmapped item"), line.Quantity));

        TheirOrderCreated created;
        try
        {
            created = await partner.CreateOrderAsync(new TheirOrderRequest(int.Parse(storeId), order.OrderId.ToString(), order.DeliveryAddress, items), ct);
        }
        catch (BrokenCircuitException)
        {
            throw new RetryLaterException(TimeSpan.FromSeconds(30), "partner circuit open"); // their outage: wait, don't burn retries
        }

        await store.RecordForwardedAsync(order.OrderId, created.OrderNo, created.Status, ct);
        logger.LogInformation("Order {OrderId} forwarded to partner as {TheirOrderNo}.", order.OrderId, created.OrderNo);
    }
}
