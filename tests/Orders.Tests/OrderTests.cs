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
}
