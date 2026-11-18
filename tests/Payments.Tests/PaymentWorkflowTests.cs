using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Payments.Api.Application;
using Payments.Api.Data;
using Payments.Api.Domain;
using Payments.Api.PaymentGateway;

namespace Payments.Tests;

/// <summary>
/// The payment rules against EF Core's in-memory database and a fake Stripe. The interesting cases are the
/// races: Stripe's "paid" and Orders' "cancelled" can arrive in either order, and each can arrive twice.
/// </summary>
public class PaymentWorkflowTests
{
    private readonly PaymentsDbContext _db = new(new DbContextOptionsBuilder<PaymentsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly FakePaymentGateway _stripe = new();
    private readonly PaymentWorkflow _workflow;
    private readonly Guid _orderId = Guid.NewGuid();

    public PaymentWorkflowTests() =>
        _workflow = new PaymentWorkflow(_db, _stripe, TimeProvider.System, NullLogger<PaymentWorkflow>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>What PaymentsController.StartCheckout does: a payment attempt with its Stripe session.</summary>
    private async Task<Payment> StartPayment()
    {
        var now = DateTime.UtcNow;
        var payment = Payment.Start(_orderId, Guid.NewGuid(), "ada@example.com", 99.49m, "usd", now, now.AddHours(1));
        var session = await _stripe.CreateCheckoutSessionAsync(new NewCheckoutSession(payment.Id, _orderId, payment.CustomerEmail,
            "usd", [new CheckoutLine("Keyboard", 99.49m, 1)], "https://shop/success", "https://shop/cancel", payment.ExpiresAtUtc), Ct);
        payment.AttachCheckoutSession(session.SessionId, session.Url!);
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(Ct);
        return payment;
    }

    /// <summary>A webhook or the customer's return: apply Stripe's view of the session, then save.</summary>
    private async Task Report(CheckoutSessionState session)
    {
        await _workflow.ApplyCheckoutSessionAsync(session, Ct);
        await _db.SaveChangesAsync(Ct);
    }

    /// <summary>The order.cancelled event (OrderCancelledHandler without the inbox).</summary>
    private async Task CancelOrder()
    {
        await _workflow.OrderCancelledAsync(_orderId, Ct);
        await _db.SaveChangesAsync(Ct);
    }

    private List<string> Events() => _db.OutboxMessages.OrderBy(m => m.OccurredOnUtc).Select(m => m.RoutingKey).ToList();

    [Fact]
    public async Task A_paid_session_marks_the_payment_succeeded_and_publishes_payment_succeeded()
    {
        var payment = await StartPayment();

        await Report(_stripe.Pay(payment.StripeSessionId!));

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.NotNull(payment.StripePaymentIntentId);
        Assert.Equal([RoutingKeys.PaymentSucceeded], Events());
    }

    [Fact]
    public async Task The_webhook_and_the_customers_return_report_the_same_payment_only_once()
    {
        var payment = await StartPayment();
        var paid = _stripe.Pay(payment.StripeSessionId!);

        await Report(paid); // the webhook
        await Report(paid); // the customer comes back to the shop
        await Report(paid); // Stripe retries the webhook

        Assert.Equal([RoutingKeys.PaymentSucceeded], Events());
    }

    [Fact]
    public async Task An_expired_session_marks_the_payment_expired_and_publishes_nothing()
    {
        var payment = await StartPayment();

        await Report(_stripe.TimeOut(payment.StripeSessionId!));

        Assert.Equal(PaymentStatus.Expired, payment.Status);
        Assert.Empty(Events());
    }

    [Fact]
    public async Task A_delayed_payment_that_fails_marks_the_attempt_failed()
    {
        var payment = await StartPayment();

        await Report(_stripe.FailLater(payment.StripeSessionId!));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Empty(Events());
    }

    [Fact]
    public async Task A_session_that_is_not_ours_is_ignored()
    {
        var result = await _workflow.ApplyCheckoutSessionAsync(
            new CheckoutSessionState("cs_test_somebody_else", null, "complete", "paid", "pi_x"), Ct);

        Assert.Null(result);
        Assert.Empty(Events());
    }

    [Fact]
    public async Task Cancelling_an_unpaid_order_closes_its_stripe_page()
    {
        var payment = await StartPayment();

        await CancelOrder();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal([payment.StripeSessionId!], _stripe.ExpiredSessions);
        Assert.Empty(_stripe.Refunds);
        Assert.Empty(Events());
    }

    [Fact]
    public async Task Cancelling_a_paid_order_refunds_it()
    {
        var payment = await StartPayment();
        await Report(_stripe.Pay(payment.StripeSessionId!));

        await CancelOrder();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Single(_stripe.Refunds);
        Assert.Equal([RoutingKeys.PaymentSucceeded, RoutingKeys.PaymentRefunded], Events());
    }

    [Fact]
    public async Task A_payment_made_just_before_the_cancel_is_refunded_even_before_its_webhook_arrives()
    {
        // The customer pays; before Stripe's webhook reaches us, order.cancelled does. Closing the page fails
        // (it's already paid), so Payments asks Stripe, sees "paid", and refunds at once.
        var payment = await StartPayment();
        var paid = _stripe.Pay(payment.StripeSessionId!);

        await CancelOrder();
        await Report(paid); // the late webhook changes nothing

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Single(_stripe.Refunds);
        Assert.Equal([RoutingKeys.PaymentRefunded], Events()); // Orders never hears "paid" for a cancelled order
    }

    [Fact]
    public async Task A_cancel_arriving_twice_refunds_once()
    {
        var payment = await StartPayment();
        await Report(_stripe.Pay(payment.StripeSessionId!));

        await CancelOrder();
        await CancelOrder();

        Assert.Single(_stripe.Refunds);
        Assert.Single(_db.CancelledOrders);
    }
}
