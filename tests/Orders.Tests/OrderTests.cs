using Orders.Api.Domain;

namespace Orders.Tests;

public class OrderTests
{
    private static Order NewOrder() => Order.Place(Guid.NewGuid(), "ada@example.com",
    [
        new OrderLine(Guid.NewGuid(), "Keyboard", 79.99m, 1),
        new OrderLine(Guid.NewGuid(), "Mouse", 24.50m, 2),
    ]);

    [Fact]
    public void A_new_order_is_pending_with_the_total_of_its_lines()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(128.99m, order.Total);
    }

    [Fact]
    public void An_order_needs_at_least_one_line() =>
        Assert.Throws<ArgumentException>(() => Order.Place(Guid.NewGuid(), "ada@example.com", []));

    [Fact]
    public void Confirm_moves_a_pending_order_to_confirmed()
    {
        var order = NewOrder();

        Assert.True(order.Confirm());
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.NotNull(order.CompletedAtUtc);
    }

    [Fact]
    public void Confirming_twice_is_harmless_and_reports_no_change()
    {
        var order = NewOrder();
        order.Confirm();

        Assert.False(order.Confirm());
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Reject_records_the_reason()
    {
        var order = NewOrder();

        Assert.True(order.Reject("Out of stock"));
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("Out of stock", order.RejectionReason);
    }

    [Fact]
    public void A_confirmed_order_cannot_be_rejected()
    {
        var order = NewOrder();
        order.Confirm();

        Assert.Throws<InvalidOperationException>(() => order.Reject("too late"));
    }

    [Fact]
    public void A_pending_order_can_be_cancelled()
    {
        var order = NewOrder();

        Assert.True(order.Cancel());
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.CancelledAtUtc);
    }

    [Fact]
    public void A_confirmed_order_can_be_cancelled()
    {
        var order = NewOrder();
        order.Confirm();

        Assert.True(order.Cancel());
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancelling_twice_is_harmless_and_reports_no_change()
    {
        var order = NewOrder();
        order.Cancel();

        Assert.False(order.Cancel());
    }

    [Fact]
    public void A_rejected_order_cannot_be_cancelled()
    {
        var order = NewOrder();
        order.Reject("Out of stock");

        Assert.Throws<InvalidOperationException>(() => order.Cancel());
    }

    [Fact]
    public void Stock_results_arriving_after_a_cancel_are_ignored()
    {
        // The customer cancelled while Catalog was still reserving: the late result must not revive the order.
        var order = NewOrder();
        order.Cancel();

        Assert.False(order.Confirm());
        Assert.False(order.Reject("too late"));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
