using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Ordering.Domain;
using FoodDelivery.Modules.Ordering.PublicApi;
using FoodDelivery.Modules.PartnerIntegration.Adapter;
using FoodDelivery.Modules.PartnerIntegration.Translation;

namespace FoodDelivery.Tests;

// =============================================================================================
// Q3 + CHALLENGE rules, tested as plain logic (no database needed).
// =============================================================================================
public class Q3ResilienceTests
{
    private static Order NewOrder() => Order.Place(Guid.NewGuid(), Guid.NewGuid(), "1 Main Rd",
        [(Guid.NewGuid(), "Burger", new Money(80m, "ZAR"), 1)], new Money(20m, "ZAR"));

    [Fact]
    public void A_new_order_waits_for_payment_and_cannot_reach_the_restaurant_yet()
    {
        var order = NewOrder();
        Assert.Equal(OrderStatus.PaymentPending, order.Status);
        Assert.True(order.IsWaitingForPayment);
        Assert.Throws<OrderRuleException>(order.Accept); // restaurant only sees PAID orders
    }

    [Fact]
    public void A_late_payment_success_for_a_failed_order_means_release_the_hold()
    {
        var order = NewOrder();
        order.MarkPaymentFailed("payment_not_confirmed"); // we gave up after 10 minutes
        Assert.True(order.NoLongerNeedsPayment);           // -> Ordering publishes OrderPaymentAbandoned -> void
        Assert.Throws<OrderRuleException>(order.MarkPaymentAuthorised); // it can never be revived
    }

    [Fact]
    public void Payment_jobs_run_in_their_own_lane()
    {
        var authorise = typeof(Modules.Payments.PaymentsModule).Assembly.GetType("FoodDelivery.Modules.Payments.Features.Authorise.AuthoriseOnOrderPlaced")!;
        Assert.Equal(JobQueues.Payments, JobQueues.QueueFor(authorise)); // bulkhead
    }
}

public class PartnerTranslationTests
{
    [Fact]
    public void Prices_in_cents_become_money()
    {
        Assert.Equal(new Money(75.00m, "ZAR"), PartnerTranslator.FromCents(7500));
    }

    [Fact]
    public void Missing_stock_flag_means_not_available()
    {
        var store = new TheirStore(1, "Bob's", "3 Kloof St", "Cape Town", true, 1500,
            [new TheirSection("Mains", [new TheirProduct("A", "Burger", 7500, null), new TheirProduct("B", "Chips", 2500, 1)])]);
        var ours = PartnerTranslator.ToOurRestaurant(store, Guid.NewGuid(), _ => Guid.NewGuid());
        Assert.False(ours.Items[0].IsAvailable); // safe default: never sell what might not exist
        Assert.True(ours.Items[1].IsAvailable);
    }

    [Fact]
    public void Statuses_are_mapped_explicitly_and_unknown_ones_are_never_guessed()
    {
        Assert.Equal(PartnerOrderUpdate.OutForDelivery, PartnerTranslator.ToOurUpdate("DISPATCHED"));
        Assert.Throws<UnknownPartnerStatusException>(() => PartnerTranslator.ToOurUpdate("TELEPORTED"));
    }
}
