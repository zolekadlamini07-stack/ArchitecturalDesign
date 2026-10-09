using FoodDelivery.BuildingBlocks;

namespace FoodDelivery.Modules.Ordering.Domain;

/// <summary>
/// Every status an order can be in during Q1.
///   PAYMENT_FAILED <- PLACED -> AWAITING_ACCEPTANCE -> ACCEPTED -> PREPARING -> READY_FOR_PICKUP -> OUT_FOR_DELIVERY -> DELIVERED
///                                       |
///                                       v
///                                    REJECTED
/// </summary>
internal enum OrderStatus
{
    Placed,
    PaymentFailed,
    AwaitingAcceptance,
    Rejected,
    Accepted,
    Preparing,
    ReadyForPickup,
    OutForDelivery,
    Delivered,
}

// ============================================================================================
// THE ORDER AGGREGATE  - the one place where order rules live.
//
// This is the "pragmatic" part of our Vertical Slice Architecture (D2):
//   Strict VSA says slices share nothing. But "can a REJECTED order be ACCEPTED?" must have
//   exactly ONE answer, so every Ordering slice goes through this class to change an order.
//
// DDD calls this an AGGREGATE: a cluster of data (order + lines + history) changed together
// through one "guard" that enforces the rules. Analogy: a bank teller - you don't change your
// balance yourself, you ask the teller, who checks the rules first.
//
// A-FRAME idea: this class is PURE logic. No database, no HTTP, no clock injection needed beyond a
// timestamp - so the rules can be unit-tested in isolation. Slice handlers do the I/O around it.
// ============================================================================================
internal sealed class Order
{
    /// <summary>
    /// THE STATE MACHINE: from each status, which statuses may come next. Anything not listed is illegal.
    /// Like a board-game track: from PREPARING you can move to READY_FOR_PICKUP, never back to AWAITING_ACCEPTANCE.
    /// </summary>
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Placed] = [OrderStatus.AwaitingAcceptance, OrderStatus.PaymentFailed],
        [OrderStatus.AwaitingAcceptance] = [OrderStatus.Accepted, OrderStatus.Rejected],
        [OrderStatus.Accepted] = [OrderStatus.Preparing],
        [OrderStatus.Preparing] = [OrderStatus.ReadyForPickup],
        [OrderStatus.ReadyForPickup] = [OrderStatus.OutForDelivery],
        [OrderStatus.OutForDelivery] = [OrderStatus.Delivered],
        [OrderStatus.PaymentFailed] = [],
        [OrderStatus.Rejected] = [],
        [OrderStatus.Delivered] = [],
    };

    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderStatusChange> _history = [];

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid RestaurantId { get; private set; }
    public OrderStatus Status { get; private set; }
    public string DeliveryAddress { get; private set; } = "";   // SNAPSHOT of the customer's address
    public decimal ItemsTotal { get; private set; }
    public decimal DeliveryFee { get; private set; }
    public decimal PlatformFee { get; private set; }
    public decimal Total { get; private set; }
    public string Currency { get; private set; } = "ZAR";
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;
    public IReadOnlyList<OrderStatusChange> History => _history;

    private Order() { } // for EF Core

    /// <summary>Creates a new order from SERVER-SIDE prices (never the client's).</summary>
    public static Order Place(Guid customerId, Guid restaurantId, string deliveryAddress,
        IReadOnlyList<(Guid ItemId, string Name, Money UnitPrice, int Quantity)> items, Money deliveryFee)
    {
        if (items.Count == 0) throw new OrderRuleException("An order needs at least one item.");
        if (items.Any(i => i.Quantity <= 0)) throw new OrderRuleException("Quantities must be positive.");

        var pricing = OrderPricing.Calculate(items.Select(i => (i.UnitPrice, i.Quantity)).ToList(), deliveryFee);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            RestaurantId = restaurantId,
            DeliveryAddress = deliveryAddress,
            Status = OrderStatus.Placed,
            ItemsTotal = pricing.ItemsTotal.Amount,
            DeliveryFee = pricing.DeliveryFee.Amount,
            PlatformFee = pricing.PlatformFee.Amount,
            Total = pricing.Total.Amount,
            Currency = pricing.Total.Currency,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var i in items)
            order._lines.Add(new OrderLine(order.Id, i.ItemId, i.Name, i.UnitPrice.Amount, i.Quantity));
        order._history.Add(new OrderStatusChange(order.Id, null, OrderStatus.Placed, "customer"));
        return order;
    }

    public Money TotalAsMoney => new(Total, Currency);

    // Each business action is a named method. Slices call THESE - never "order.Status = ...".
    public void MarkPaymentAuthorised() => TransitionTo(OrderStatus.AwaitingAcceptance, "payments");
    public void MarkPaymentFailed() => TransitionTo(OrderStatus.PaymentFailed, "payments");
    public void Accept() => TransitionTo(OrderStatus.Accepted, "restaurant");
    public void Reject(string reason) { RejectionReason = reason; TransitionTo(OrderStatus.Rejected, "restaurant"); }
    public void StartPreparing() => TransitionTo(OrderStatus.Preparing, "restaurant");
    public void MarkReadyForPickup() => TransitionTo(OrderStatus.ReadyForPickup, "restaurant");
    public void MarkOutForDelivery() => TransitionTo(OrderStatus.OutForDelivery, "driver");
    public void MarkDelivered() => TransitionTo(OrderStatus.Delivered, "driver");

    private void TransitionTo(OrderStatus next, string changedBy)
    {
        if (!AllowedTransitions[Status].Contains(next))
            throw new OrderRuleException($"An order cannot go from {Status} to {next}.");
        _history.Add(new OrderStatusChange(Id, Status, next, changedBy));
        Status = next;
    }
}

/// <summary>
/// One line of an order. Name and price are a SNAPSHOT taken at order time, so if the menu price
/// changes tomorrow, this order (like a receipt) still shows what the customer actually paid.
/// </summary>
internal sealed class OrderLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public string ItemName { get; private set; } = "";
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }

    private OrderLine() { }
    public OrderLine(Guid orderId, Guid menuItemId, string itemName, decimal unitPrice, int quantity) =>
        (OrderId, MenuItemId, ItemName, UnitPrice, Quantity) = (orderId, menuItemId, itemName, unitPrice, quantity);
}

/// <summary>Every status change, who made it and when - the order's audit trail.</summary>
internal sealed class OrderStatusChange
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public OrderStatus? FromStatus { get; private set; }
    public OrderStatus ToStatus { get; private set; }
    public string ChangedBy { get; private set; } = "";
    public DateTimeOffset ChangedAt { get; private set; } = DateTimeOffset.UtcNow;

    private OrderStatusChange() { }
    public OrderStatusChange(Guid orderId, OrderStatus? from, OrderStatus to, string changedBy) =>
        (OrderId, FromStatus, ToStatus, ChangedBy) = (orderId, from, to, changedBy);
}

/// <summary>Thrown when a slice asks the order to break a business rule.</summary>
internal sealed class OrderRuleException(string message) : Exception(message);
