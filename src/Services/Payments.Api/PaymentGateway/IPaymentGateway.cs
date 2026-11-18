namespace Payments.Api.PaymentGateway;

/// <summary>Settings in the "Stripe" section. The key comes from .env (Docker) or user secrets, never from git.</summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>A TEST secret key (sk_test_... or rk_test_...). Live keys are refused on purpose.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>The webhook signing secret (whsec_...), if you set it by hand.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Or a file that contains it. In Docker the stripe-cli container writes its secret there, so you don't
    /// have to copy it anywhere (see docker-compose.yml).
    /// </summary>
    public string WebhookSecretFile { get; set; } = string.Empty;

    public string Currency { get; set; } = "usd";
}

/// <summary>Settings in the "Payments" section.</summary>
public class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>Where Stripe sends the customer back to after paying (or giving up): the Angular app.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>How long a Checkout Session stays payable. Stripe allows 30 minutes to 24 hours.</summary>
    public int CheckoutLifetimeMinutes { get; set; } = 60;
}

public record CheckoutLine(string Name, decimal UnitPrice, int Quantity);

/// <summary>What we ask Stripe for: a hosted payment page for these lines.</summary>
public record NewCheckoutSession(
    Guid PaymentId,
    Guid OrderId,
    string CustomerEmail,
    string Currency,
    IReadOnlyList<CheckoutLine> Lines,
    string SuccessUrl,
    string CancelUrl,
    DateTime ExpiresAtUtc);

/// <summary>What Stripe says about a Checkout Session: its status is open, complete or expired; its payment status paid or unpaid.</summary>
public record CheckoutSessionState(string SessionId, string? Url, string Status, string PaymentStatus, string? PaymentIntentId)
{
    public bool IsPaid => PaymentStatus == "paid";
    public bool IsExpired => Status == "expired";

    /// <summary>The customer finished, but a delayed payment method (a bank debit) failed later.</summary>
    public bool IsFailed => Status == "complete" && PaymentStatus == "unpaid";
}

/// <summary>A verified webhook call. CheckoutSession is set for checkout.session.* events.</summary>
public record WebhookEvent(string Id, string Type, CheckoutSessionState? CheckoutSession);

/// <summary>Stripe refused or couldn't be reached. The message is safe to show (it never contains the key).</summary>
public class PaymentGatewayException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The webhook's signature didn't match: it didn't come from Stripe, or it was changed on the way.</summary>
public class InvalidWebhookException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Everything Payments needs from a payment provider. StripePaymentGateway is the real one; the unit tests
/// use a fake, so the payment rules can be tested without calling Stripe.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>False when no (test) key is configured: checkout then answers 503 and the rest of the shop keeps working.</summary>
    bool IsConfigured { get; }

    /// <summary>False until a webhook signing secret is known.</summary>
    bool CanVerifyWebhooks { get; }

    Task<CheckoutSessionState> CreateCheckoutSessionAsync(NewCheckoutSession request, CancellationToken ct);

    Task<CheckoutSessionState> GetCheckoutSessionAsync(string sessionId, CancellationToken ct);

    /// <summary>Closes an open session so it can't be paid any more. False if it's too late (already paid or expired).</summary>
    Task<bool> TryExpireCheckoutSessionAsync(string sessionId, CancellationToken ct);

    /// <summary>Gives the whole payment back. Safe to call again for the same payment: returns the same refund. Returns the refund id.</summary>
    Task<string> RefundAsync(string paymentIntentId, Guid paymentId, CancellationToken ct);

    /// <summary>Checks the Stripe-Signature header and reads the event. Throws InvalidWebhookException if it doesn't match.</summary>
    WebhookEvent ParseWebhookEvent(string json, string signatureHeader);
}
