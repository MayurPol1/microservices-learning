using Payments.Api.Domain;
using Payments.Api.PaymentGateway;

namespace Payments.Tests;

public class PaymentTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    private static Payment NewPayment()
    {
        var payment = Payment.Start(Guid.NewGuid(), Guid.NewGuid(), "ada@example.com", 99.49m, "USD", Now, Now.AddHours(1));
        payment.AttachCheckoutSession("cs_test_1", "https://checkout.stripe.com/c/pay/cs_test_1");
        return payment;
    }

    [Fact]
    public void A_new_payment_is_open_with_a_lower_case_currency()
    {
        var payment = NewPayment();

        Assert.Equal(PaymentStatus.Open, payment.Status);
        Assert.Equal("usd", payment.Currency);
    }

    [Fact]
    public void A_payment_needs_a_positive_amount() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Payment.Start(Guid.NewGuid(), Guid.NewGuid(), "ada@example.com", 0m, "usd", Now, Now.AddHours(1)));

    [Fact]
    public void An_open_page_is_reused_until_a_minute_before_it_expires()
    {
        var payment = NewPayment();

        Assert.True(payment.CanBeReused(Now.AddMinutes(30)));
        Assert.False(payment.CanBeReused(Now.AddMinutes(59.5)));
    }

    [Fact]
    public void Succeeding_twice_reports_no_change()
    {
        var payment = NewPayment();

        Assert.True(payment.MarkSucceeded("pi_1", Now));
        Assert.False(payment.MarkSucceeded("pi_1", Now));
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public void Stripe_saying_paid_wins_over_what_we_thought()
    {
        // We had marked it expired, but Stripe reports a payment: record the money.
        var payment = NewPayment();
        payment.MarkExpired(Now);

        Assert.True(payment.MarkSucceeded("pi_1", Now));
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Null(payment.ClosedAtUtc);
    }

    [Fact]
    public void Only_an_open_payment_can_expire_or_be_cancelled()
    {
        var payment = NewPayment();
        payment.MarkSucceeded("pi_1", Now);

        Assert.False(payment.MarkExpired(Now));
        Assert.False(payment.MarkCancelled(Now));
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public void Only_a_succeeded_payment_can_be_refunded()
    {
        var payment = NewPayment();

        Assert.Throws<InvalidOperationException>(() => payment.MarkRefunded("re_1", Now));

        payment.MarkSucceeded("pi_1", Now);
        Assert.True(payment.MarkRefunded("re_1", Now));
        Assert.False(payment.MarkRefunded("re_1", Now));
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Theory]
    [InlineData("99.49", 9949)]
    [InlineData("0.5", 50)]
    [InlineData("19.995", 2000)]
    public void Amounts_go_to_stripe_in_cents(string amount, long cents) =>
        Assert.Equal(cents, StripePaymentGateway.ToMinorUnits(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
}
