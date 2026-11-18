using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payments.Api.Application;
using Payments.Api.Data;
using Payments.Api.PaymentGateway;

namespace Payments.Api.Controllers;

/// <summary>
/// Stripe calls this URL when something happens to a Checkout Session (paid, expired). Locally the
/// stripe-cli container forwards those calls here through the gateway; in production you register the
/// public URL in Stripe's Dashboard.
/// <para>
/// There is no JWT here ([AllowAnonymous]): Stripe isn't a user. The <b>signature</b> is the authentication:
/// Stripe signs the body with a secret only it and we know, and we reject anything that doesn't match.
/// </para>
/// </summary>
[ApiController]
[Route("api/payments/webhook")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class StripeWebhookController(
    IPaymentGateway gateway,
    PaymentWorkflow workflow,
    PaymentsDbContext db,
    ILogger<StripeWebhookController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (!gateway.CanVerifyWebhooks)
        {
            // 503 tells Stripe to retry later. Payments still work without webhooks: the shop asks Stripe
            // itself when the customer comes back (PaymentsController.Refresh).
            logger.LogWarning("Stripe webhook received, but no signing secret is configured yet; rejecting it");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        // The signature covers the exact bytes Stripe sent, so read the raw body (no model binding).
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(ct);

        WebhookEvent stripeEvent;
        try
        {
            stripeEvent = gateway.ParseWebhookEvent(json, Request.Headers["Stripe-Signature"].ToString());
        }
        catch (InvalidWebhookException ex)
        {
            logger.LogWarning("Rejected a webhook with an invalid signature: {Error}", ex.Message);
            return BadRequest();
        }

        if (stripeEvent.CheckoutSession is null)
        {
            logger.LogInformation("Stripe event {EventId} ({Type}) isn't about a Checkout Session; ignoring", stripeEvent.Id, stripeEvent.Type);
            return Ok();
        }

        logger.LogInformation("Stripe event {EventId}: {Type} for session {SessionId}",
            stripeEvent.Id, stripeEvent.Type, stripeEvent.CheckoutSession.SessionId);

        // Stripe delivers each event at least once and retries until it gets a 2xx. The workflow is
        // idempotent, so a repeat changes nothing. An exception here returns 500 and Stripe retries later.
        await workflow.ApplyCheckoutSessionAsync(stripeEvent.CheckoutSession, ct);
        await db.SaveChangesAsync(ct);
        return Ok();
    }
}
