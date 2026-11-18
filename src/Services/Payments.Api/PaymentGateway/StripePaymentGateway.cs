using System.Net;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Payments.Api.PaymentGateway;

/// <summary>
/// Talks to Stripe with its official SDK (Stripe.net). We use <b>Stripe Checkout</b>: Stripe hosts the payment
/// page, so card numbers never reach MicroShop. That keeps us out of most of PCI DSS (the card industry's
/// security rules), which is why almost every shop does it this way.
/// <para>
/// Every call that changes something at Stripe sends an <b>idempotency key</b> derived from our payment id. If
/// a call is retried (a timeout, a redelivered message), Stripe recognises the key and returns the first
/// result instead of creating a second session or a second refund.
/// </para>
/// </summary>
public sealed class StripePaymentGateway : IPaymentGateway
{
    private readonly StripeOptions _options;
    private readonly ILogger<StripePaymentGateway> _logger;
    private readonly IStripeClient? _client;
    private string? _webhookSecretFromFile;

    public StripePaymentGateway(IOptions<StripeOptions> options, ILogger<StripePaymentGateway> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Never log the key itself, not even part of it.
        var key = _options.SecretKey.Trim();
        if (key.Length == 0)
        {
            logger.LogWarning("Stripe: no secret key configured. Checkout answers 503 until you set STRIPE_SECRET_KEY in .env (see README)");
        }
        else if (key.StartsWith("sk_live_") || key.StartsWith("rk_live_"))
        {
            // A guard rail for a learning project: real money must never move by accident.
            logger.LogError("Stripe: a LIVE key is configured. This project only accepts test keys (sk_test_ or rk_test_), so payments are disabled");
        }
        else if (!key.StartsWith("sk_test_") && !key.StartsWith("rk_test_"))
        {
            logger.LogError("Stripe: the configured key doesn't look like a Stripe test key (sk_test_ or rk_test_), so payments are disabled");
        }
        else
        {
            _client = new StripeClient(key);
            logger.LogInformation("Stripe: test-mode key configured");
        }
    }

    public bool IsConfigured => _client is not null;

    public bool CanVerifyWebhooks => IsConfigured && WebhookSecret() is not null;

    private IStripeClient Client => _client ?? throw new PaymentGatewayException("Stripe isn't configured.");

    public async Task<CheckoutSessionState> CreateCheckoutSessionAsync(NewCheckoutSession request, CancellationToken ct)
    {
        var metadata = new Dictionary<string, string>
        {
            ["order_id"] = request.OrderId.ToString(),
            ["payment_id"] = request.PaymentId.ToString(),
        };

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            // No payment method list: Stripe shows the methods enabled in your Dashboard ("dynamic payment
            // methods"). Cards succeed or fail on the spot; slower ones (bank debits, vouchers) finish later
            // with checkout.session.async_payment_succeeded or _failed, which PaymentWorkflow handles too.
            LineItems = request.Lines.Select(line => new SessionLineItemOptions
            {
                Quantity = line.Quantity,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = request.Currency,
                    UnitAmount = ToMinorUnits(line.UnitPrice),
                    ProductData = new SessionLineItemPriceDataProductDataOptions { Name = line.Name },
                },
            }).ToList(),
            CustomerEmail = request.CustomerEmail,
            ClientReferenceId = request.OrderId.ToString(),
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata },
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ExpiresAt = request.ExpiresAtUtc,
        };

        var session = await Call(() => new SessionService(Client).CreateAsync(
            options, new RequestOptions { IdempotencyKey = $"checkout-{request.PaymentId}" }, ct));

        _logger.LogInformation("Stripe Checkout Session {SessionId} created for order {OrderId}", session.Id, request.OrderId);
        return ToState(session);
    }

    public async Task<CheckoutSessionState> GetCheckoutSessionAsync(string sessionId, CancellationToken ct)
    {
        var session = await Call(() => new SessionService(Client).GetAsync(sessionId, cancellationToken: ct));
        return ToState(session);
    }

    public async Task<bool> TryExpireCheckoutSessionAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            await new SessionService(Client).ExpireAsync(sessionId, cancellationToken: ct);
            _logger.LogInformation("Stripe Checkout Session {SessionId} expired", sessionId);
            return true;
        }
        catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.BadRequest)
        {
            // Stripe only expires open sessions. This one was just paid, or had already expired.
            _logger.LogInformation("Stripe Checkout Session {SessionId} can't be expired any more: {Error}", sessionId, ex.Message);
            return false;
        }
        catch (StripeException ex)
        {
            throw new PaymentGatewayException($"Stripe error: {ex.Message}", ex);
        }
    }

    public async Task<string> RefundAsync(string paymentIntentId, Guid paymentId, CancellationToken ct)
    {
        var options = new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId,
            Metadata = new Dictionary<string, string> { ["payment_id"] = paymentId.ToString() },
        };

        var refund = await Call(() => new RefundService(Client).CreateAsync(
            options, new RequestOptions { IdempotencyKey = $"refund-{paymentId}" }, ct));

        _logger.LogInformation("Stripe refund {RefundId} created for payment {PaymentId}", refund.Id, paymentId);
        return refund.Id;
    }

    public WebhookEvent ParseWebhookEvent(string json, string signatureHeader)
    {
        var secret = WebhookSecret() ?? throw new InvalidWebhookException("No webhook signing secret configured.");

        Event stripeEvent;
        try
        {
            // Checks the HMAC signature in the Stripe-Signature header against the signing secret, and that
            // the timestamp is recent (replay protection). Without this check, anyone could POST
            // "payment succeeded" to this public URL and get free goods.
            stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            throw new InvalidWebhookException(ex.Message, ex);
        }

        var session = stripeEvent.Data.Object as Session;
        return new WebhookEvent(stripeEvent.Id, stripeEvent.Type, session is null ? null : ToState(session));
    }

    /// <summary>
    /// Stripe counts money in the currency's smallest unit: $12.34 is 1234 cents. (Zero-decimal currencies
    /// such as JPY would skip the * 100; this shop only uses USD.)
    /// </summary>
    public static long ToMinorUnits(decimal amount) =>
        (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static CheckoutSessionState ToState(Session session) =>
        new(session.Id, session.Url, session.Status, session.PaymentStatus, session.PaymentIntentId);

    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (StripeException ex)
        {
            throw new PaymentGatewayException($"Stripe error: {ex.Message}", ex);
        }
    }

    /// <summary>The signing secret: from configuration, or from the file the stripe-cli container writes (read once it exists).</summary>
    private string? WebhookSecret()
    {
        if (!string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            return _options.WebhookSecret.Trim();
        }

        if (_webhookSecretFromFile is not null || string.IsNullOrWhiteSpace(_options.WebhookSecretFile))
        {
            return _webhookSecretFromFile;
        }

        try
        {
            var text = System.IO.File.Exists(_options.WebhookSecretFile) ? System.IO.File.ReadAllText(_options.WebhookSecretFile).Trim() : string.Empty;
            if (text.StartsWith("whsec_"))
            {
                _webhookSecretFromFile = text;
                _logger.LogInformation("Stripe: webhook signing secret loaded from {File}", _options.WebhookSecretFile);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Stripe: can't read {File}: {Error}", _options.WebhookSecretFile, ex.Message);
        }

        return _webhookSecretFromFile;
    }
}
