using BuildingBlocks.Messaging.Outbox;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Payments.Api.Data;
using Payments.Api.Domain;
using Payments.Api.PaymentGateway;

namespace Payments.Api.Application;

/// <summary>
/// The payment rules, in one place, for the three ways news reaches Payments:
/// <list type="bullet">
/// <item>Stripe's webhook ("this session was paid / expired"),</item>
/// <item>the customer coming back from Stripe's page (we ask Stripe ourselves, in case the webhook is late or not set up),</item>
/// <item>the order.cancelled event from Orders.</item>
/// </list>
/// Each can arrive more than once and in any order, so every step is idempotent. Methods change the
/// DbContext and the outbox; the caller saves, so a state change and its event are committed together.
/// </summary>
public class PaymentWorkflow(PaymentsDbContext db, IPaymentGateway gateway, TimeProvider time, ILogger<PaymentWorkflow> logger)
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Applies what Stripe says about a Checkout Session. Returns the payment, or null for a session we don't know.</summary>
    public async Task<Payment?> ApplyCheckoutSessionAsync(CheckoutSessionState session, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.StripeSessionId == session.SessionId, ct);
        if (payment is null)
        {
            logger.LogWarning("Stripe session {SessionId} doesn't belong to any MicroShop payment; ignoring", session.SessionId);
            return null;
        }

        if (session.IsPaid)
        {
            if (payment.MarkSucceeded(session.PaymentIntentId, Now))
            {
                await OnSucceededAsync(payment, ct);
            }
        }
        else if (session.IsExpired && payment.MarkExpired(Now))
        {
            logger.LogInformation("Payment {PaymentId} for order {OrderId} expired unpaid", payment.Id, payment.OrderId);
        }
        else if (session.IsFailed && payment.MarkFailed(Now))
        {
            logger.LogInformation("Payment {PaymentId} for order {OrderId} failed after checkout", payment.Id, payment.OrderId);
        }

        return payment;
    }

    /// <summary>
    /// The order was cancelled: close any open Checkout Session and refund any money already taken. The
    /// cancellation is also remembered, so a payment that completes later is refunded too.
    /// </summary>
    public async Task OrderCancelledAsync(Guid orderId, CancellationToken ct)
    {
        if (!await IsOrderCancelledAsync(orderId, ct))
        {
            db.CancelledOrders.Add(new CancelledOrder(orderId, Now));
        }

        var payments = await db.Payments.Where(p => p.OrderId == orderId).ToListAsync(ct);
        foreach (var payment in payments)
        {
            switch (payment.Status)
            {
                case PaymentStatus.Open:
                    await CloseOpenPaymentAsync(payment, ct);
                    break;
                case PaymentStatus.Succeeded:
                    await RefundAsync(payment, ct);
                    break;
            }
        }
    }

    /// <summary>
    /// Also looks at <c>Local</c>: the cancellation may have been added in this same unit of work and not saved yet
    /// (a query only sees what's already in the database).
    /// </summary>
    public async Task<bool> IsOrderCancelledAsync(Guid orderId, CancellationToken ct) =>
        db.CancelledOrders.Local.Any(c => c.OrderId == orderId)
        || await db.CancelledOrders.AnyAsync(c => c.OrderId == orderId, ct);

    private async Task CloseOpenPaymentAsync(Payment payment, CancellationToken ct)
    {
        if (payment.StripeSessionId is null || await gateway.TryExpireCheckoutSessionAsync(payment.StripeSessionId, ct))
        {
            payment.MarkCancelled(Now);
            logger.LogInformation("Payment {PaymentId} closed: order {OrderId} was cancelled before it was paid", payment.Id, payment.OrderId);
            return;
        }

        // Too late to close it: the customer paid a moment ago (or it had just expired). Ask Stripe what happened.
        var session = await gateway.GetCheckoutSessionAsync(payment.StripeSessionId, ct);
        await ApplyCheckoutSessionAsync(session, ct);
    }

    private async Task OnSucceededAsync(Payment payment, CancellationToken ct)
    {
        if (await IsOrderCancelledAsync(payment.OrderId, ct))
        {
            // Paid for an order that no longer exists: give the money straight back. Orders never hears
            // "payment.succeeded" for it, so the order stays Cancelled.
            logger.LogWarning("Payment {PaymentId} succeeded for cancelled order {OrderId}; refunding", payment.Id, payment.OrderId);
            await RefundAsync(payment, ct);
            return;
        }

        db.AddToOutbox(RoutingKeys.PaymentSucceeded,
            new PaymentSucceeded(payment.OrderId, payment.Id, payment.CustomerEmail, payment.Amount, payment.Currency));
        logger.LogInformation("Payment {PaymentId} succeeded: order {OrderId} is paid ({Amount} {Currency})",
            payment.Id, payment.OrderId, payment.Amount, payment.Currency);
    }

    private async Task RefundAsync(Payment payment, CancellationToken ct)
    {
        if (payment.StripePaymentIntentId is null)
        {
            throw new InvalidOperationException($"Payment {payment.Id} has no Stripe payment intent, so it can't be refunded.");
        }

        // The external call happens before our transaction commits. If the commit then fails, the message or
        // webhook is retried, and the same idempotency key makes Stripe return this refund instead of a second one.
        var refundId = await gateway.RefundAsync(payment.StripePaymentIntentId, payment.Id, ct);
        if (payment.MarkRefunded(refundId, Now))
        {
            db.AddToOutbox(RoutingKeys.PaymentRefunded,
                new PaymentRefunded(payment.OrderId, payment.Id, payment.CustomerEmail, payment.Amount, payment.Currency));
            logger.LogInformation("Payment {PaymentId} for order {OrderId} refunded", payment.Id, payment.OrderId);
        }
    }
}
