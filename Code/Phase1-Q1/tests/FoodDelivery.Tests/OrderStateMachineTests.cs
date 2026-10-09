using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.Domain;

namespace FoodDelivery.Tests;

// =============================================================================================
// The Order aggregate is PURE logic (no database, no HTTP), so its rules are tested directly.
// This is the A-Frame idea: keep the decisions separate from the I/O, and testing becomes trivial.
// (The Ordering project grants this test project access to its internals via InternalsVisibleTo;
// other MODULES still cannot see them.)
// =============================================================================================
public class OrderStateMachineTests
{
    private static Order NewOrder() => Order.Place(
        Guid.NewGuid(), Guid.NewGuid(), "1 Main Rd, Cape Town, 8001",
        [(Guid.NewGuid(), "Burger", new Money(80m, "ZAR"), 2)],
        new Money(20m, "ZAR"));

    [Fact]
    public void Pricing_follows_the_documented_formula()
    {
        var order = NewOrder();
        // items 160 + delivery 20 = 180; platform fee 10% = 18; total 198
        Assert.Equal(160m, order.ItemsTotal);
        Assert.Equal(18m, order.PlatformFee);
        Assert.Equal(198m, order.Total);
    }

    [Fact]
    public void Happy_path_walks_every_status_in_order()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorised();
        order.Accept();
        order.StartPreparing();
        order.MarkReadyForPickup();
        order.MarkOutForDelivery();
        order.MarkDelivered();

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(7, order.History.Count); // placed + 6 changes, every one recorded
    }

    [Fact]
    public void A_rejected_order_can_never_be_accepted()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorised();
        order.Reject("Out of stock");
        Assert.Throws<OrderRuleException>(order.Accept);
    }

    [Fact]
    public void The_restaurant_never_sees_an_unpaid_order_as_acceptable()
    {
        var order = NewOrder(); // still PLACED: payment not authorised yet
        Assert.Throws<OrderRuleException>(order.Accept);
    }
}
