using Payments.Api.PaymentGateway;

namespace Payments.Tests;

/// <summary>
/// Stands in for Stripe: keeps sessions in a dictionary and records what was asked of it.
/// Tests "pay" a session by calling <see cref="Pay"/>, the way a customer would on Stripe's page.
/// </summary>
public class FakePaymentGateway : IPaymentGateway
{
    private readonly Dictionary<string, CheckoutSessionState> _sessions = [];

    public List<string> ExpiredSessions { get; } = [];
    public List<(string PaymentIntentId, Guid PaymentId)> Refunds { get; } = [];

    public bool IsConfigured => true;
    public bool CanVerifyWebhooks => true;

    public Task<CheckoutSessionState> CreateCheckoutSessionAsync(NewCheckoutSession request, CancellationToken ct)
    {
        var id = $"cs_test_{Guid.NewGuid():N}";
        var session = new CheckoutSessionState(id, $"https://checkout.stripe.test/{id}", "open", "unpaid", null);
        _sessions[id] = session;
        return Task.FromResult(session);
    }

    public Task<CheckoutSessionState> GetCheckoutSessionAsync(string sessionId, CancellationToken ct) =>
        Task.FromResult(_sessions[sessionId]);

    public Task<bool> TryExpireCheckoutSessionAsync(string sessionId, CancellationToken ct)
    {
        var session = _sessions[sessionId];
        if (session.Status != "open")
        {
            return Task.FromResult(false);
        }

        _sessions[sessionId] = session with { Status = "expired" };
        ExpiredSessions.Add(sessionId);
        return Task.FromResult(true);
    }

    public Task<string> RefundAsync(string paymentIntentId, Guid paymentId, CancellationToken ct)
    {
        Refunds.Add((paymentIntentId, paymentId));
        return Task.FromResult($"re_test_{paymentId:N}");
    }

    public WebhookEvent ParseWebhookEvent(string json, string signatureHeader) =>
        throw new NotSupportedException("Webhook parsing is Stripe.net's job; these tests call the workflow directly.");

    /// <summary>The customer pays on Stripe's page. Returns the session as the webhook would report it.</summary>
    public CheckoutSessionState Pay(string sessionId)
    {
        var paid = _sessions[sessionId] with { Status = "complete", PaymentStatus = "paid", PaymentIntentId = $"pi_test_{sessionId}" };
        _sessions[sessionId] = paid;
        return paid;
    }

    /// <summary>The customer chose a bank debit that failed after checkout.</summary>
    public CheckoutSessionState FailLater(string sessionId)
    {
        var failed = _sessions[sessionId] with { Status = "complete", PaymentStatus = "unpaid" };
        _sessions[sessionId] = failed;
        return failed;
    }

    /// <summary>Stripe expires the session because nobody paid in time.</summary>
    public CheckoutSessionState TimeOut(string sessionId)
    {
        var expired = _sessions[sessionId] with { Status = "expired" };
        _sessions[sessionId] = expired;
        return expired;
    }
}
