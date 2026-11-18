namespace Payments.Api.Domain;

public enum PaymentStatus
{
    /// <summary>A Stripe Checkout Session exists and the customer can pay on Stripe's page.</summary>
    Open,

    /// <summary>Stripe took the money.</summary>
    Succeeded,

    /// <summary>The Checkout Session timed out without a payment. The customer can start a new one.</summary>
    Expired,

    /// <summary>A delayed payment method (a bank debit) failed after checkout. The customer can try again.</summary>
    Failed,

    /// <summary>The order was cancelled before it was paid; we closed the Checkout Session.</summary>
    Cancelled,

    /// <summary>The order was cancelled after it was paid; the money went back to the customer.</summary>
    Refunded,
}

/// <summary>
/// One attempt to pay for an order. An order can have several attempts (the first Checkout Session
/// expired, the customer tried again), but at most one of them ever succeeds.
/// <para>
/// Payments owns this data. Orders only learns the outcome, through the payment.succeeded event.
/// </para>
/// </summary>
public class Payment
{
    public const int CurrencyMaxLength = 3;
    public const int StripeIdMaxLength = 255;
    public const int UrlMaxLength = 2048;

    private Payment() { } // for EF Core

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }

    /// <summary>The order's owner (the "sub" claim of whoever placed it): only they and admins see this payment.</summary>
    public Guid CustomerId { get; private set; }

    public string CustomerEmail { get; private set; } = string.Empty;

    /// <summary>Copied from the order when the payment starts. Never taken from the browser.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }

    public string? StripeSessionId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public string? StripePaymentIntentId { get; private set; }
    public string? StripeRefundId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public DateTime? RefundedAtUtc { get; private set; }

    /// <summary>When the attempt ended without money changing hands (Expired, Failed or Cancelled).</summary>
    public DateTime? ClosedAtUtc { get; private set; }

    public static Payment Start(Guid orderId, Guid customerId, string customerEmail, decimal amount, string currency,
        DateTime nowUtc, DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            Amount = amount,
            Currency = currency.ToLowerInvariant(),
            Status = PaymentStatus.Open,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    /// <summary>Links this attempt to the Checkout Session Stripe created for it.</summary>
    public void AttachCheckoutSession(string sessionId, string checkoutUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkoutUrl);
        StripeSessionId = sessionId;
        CheckoutUrl = checkoutUrl;
    }

    /// <summary>
    /// True if the customer can still be sent to this attempt's Stripe page, so clicking "Pay" twice
    /// doesn't create two sessions. Leaves a minute of margin before Stripe expires it.
    /// </summary>
    public bool CanBeReused(DateTime nowUtc) =>
        Status == PaymentStatus.Open && CheckoutUrl is not null && ExpiresAtUtc > nowUtc.AddMinutes(1);

    /// <summary>
    /// Records that Stripe took the money. Returns false if that was already recorded: Stripe sends
    /// webhooks at least once, and the customer's return to the shop reports the same session again.
    /// <para>
    /// Stripe is the source of truth for money: if it says "paid", we record paid, even if we thought
    /// the attempt was expired or cancelled. What to do about it (refund) is decided by the caller.
    /// </para>
    /// </summary>
    public bool MarkSucceeded(string? paymentIntentId, DateTime nowUtc)
    {
        if (Status is PaymentStatus.Succeeded or PaymentStatus.Refunded)
        {
            return false;
        }

        Status = PaymentStatus.Succeeded;
        StripePaymentIntentId = paymentIntentId;
        PaidAtUtc = nowUtc;
        ClosedAtUtc = null;
        return true;
    }

    /// <summary>Stripe expired the session unpaid. Returns false unless the attempt was still open.</summary>
    public bool MarkExpired(DateTime nowUtc) => Close(PaymentStatus.Expired, nowUtc);

    /// <summary>A delayed payment failed. Returns false unless the attempt was still open.</summary>
    public bool MarkFailed(DateTime nowUtc) => Close(PaymentStatus.Failed, nowUtc);

    /// <summary>We closed the session because the order was cancelled. Returns false unless it was still open.</summary>
    public bool MarkCancelled(DateTime nowUtc) => Close(PaymentStatus.Cancelled, nowUtc);

    /// <summary>The money went back to the customer. Returns false if that was already recorded.</summary>
    public bool MarkRefunded(string refundId, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refundId);
        if (Status == PaymentStatus.Refunded)
        {
            return false;
        }

        if (Status != PaymentStatus.Succeeded)
        {
            throw new InvalidOperationException($"Payment {Id} is {Status}; only a succeeded payment can be refunded.");
        }

        Status = PaymentStatus.Refunded;
        StripeRefundId = refundId;
        RefundedAtUtc = nowUtc;
        return true;
    }

    private bool Close(PaymentStatus status, DateTime nowUtc)
    {
        if (Status != PaymentStatus.Open)
        {
            return false;
        }

        Status = status;
        ClosedAtUtc = nowUtc;
        return true;
    }
}

/// <summary>
/// "This order was cancelled", remembered from the order.cancelled event. Payments checks it whenever a
/// payment succeeds, so money taken for a cancelled order is refunded straight away, whichever arrives
/// first: the cancellation or Stripe's "paid".
/// </summary>
public class CancelledOrder
{
    private CancelledOrder() { } // for EF Core

    public CancelledOrder(Guid orderId, DateTime cancelledAtUtc)
    {
        OrderId = orderId;
        CancelledAtUtc = cancelledAtUtc;
    }

    public Guid OrderId { get; private set; }
    public DateTime CancelledAtUtc { get; private set; }
}
