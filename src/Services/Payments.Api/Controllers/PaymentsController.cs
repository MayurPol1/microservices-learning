using BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.Api.Application;
using Payments.Api.Clients;
using Payments.Api.Data;
using Payments.Api.Domain;
using Payments.Api.Models;
using Payments.Api.PaymentGateway;

namespace Payments.Api.Controllers;

/// <summary>
/// Paying for an order with Stripe Checkout:
/// <list type="number">
/// <item>POST /api/payments/checkout: we ask Stripe for a payment page and send its URL back.</item>
/// <item>The browser goes to Stripe, the customer pays there (test card 4242 4242 4242 4242).</item>
/// <item>Stripe tells us through the webhook (StripeWebhookController), and sends the customer back to the shop,
/// which calls POST /api/payments/sessions/{id}/refresh as a second, faster way to find out.</item>
/// <item>We publish payment.succeeded; Orders marks the order Paid.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/payments")]
[Produces("application/json")]
[Authorize]
public class PaymentsController(
    PaymentsDbContext db,
    PaymentWorkflow workflow,
    IPaymentGateway gateway,
    OrdersClient orders,
    IOptions<StripeOptions> stripeOptions,
    IOptions<PaymentsOptions> paymentsOptions,
    TimeProvider time,
    ILogger<PaymentsController> logger) : ControllerBase
{
    /// <summary>
    /// Starts paying for a Confirmed order (its stock is reserved) and returns the Stripe page to send the
    /// customer to. Clicking "Pay" again while that page is still valid returns the same page.
    /// </summary>
    [HttpPost("checkout")]
    [ProducesResponseType<StartCheckoutResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<StartCheckoutResponse>> StartCheckout(StartCheckoutRequest request, CancellationToken ct)
    {
        // Ask Orders for the order as the caller (their token is passed on): Orders applies its ownership
        // rules, and the amount comes from Orders' database, not from the browser.
        OrderInfo? order;
        try
        {
            order = await orders.GetOrderAsync(request.OrderId, Request.Headers.Authorization.ToString(), ct);
        }
        catch (OrdersUnavailableException ex)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Orders unavailable", detail: ex.Message);
        }

        if (order is null)
        {
            return NotFound();
        }

        if (order.CustomerId != User.GetUserId())
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not your order",
                detail: "Only the customer who placed the order can pay for it.");
        }

        if (order.Status != "Confirmed" || await workflow.IsOrderCancelledAsync(order.Id, ct))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This order can't be paid now", detail: order.Status switch
            {
                "Pending" => "Catalog is still reserving the stock. Try again in a moment.",
                "Paid" => "This order is already paid.",
                "Rejected" => "This order was rejected, so there is nothing to pay.",
                _ => "This order was cancelled.",
            });
        }

        var now = time.GetUtcNow().UtcDateTime;
        var attempts = await db.Payments.Where(p => p.OrderId == order.Id).ToListAsync(ct);

        if (attempts.Any(p => p.Status is PaymentStatus.Succeeded or PaymentStatus.Refunded))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This order can't be paid now",
                detail: "This order is already paid; Orders will show it in a moment.");
        }

        var reusable = attempts.FirstOrDefault(p => p.CanBeReused(now));
        if (reusable is not null)
        {
            return new StartCheckoutResponse(reusable.Id, reusable.CheckoutUrl!, reusable.ExpiresAtUtc);
        }

        // Checked last, so a request that is wrong anyway (someone else's order, an unpaid-able status) gets that
        // answer, with or without Stripe.
        if (!gateway.IsConfigured)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Payments are not set up",
                detail: "Stripe isn't configured: put your Stripe TEST secret key in .env as STRIPE_SECRET_KEY and restart (see the README).");
        }

        var settings = paymentsOptions.Value;
        var payment = Payment.Start(order.Id, order.CustomerId, order.CustomerEmail, order.Total, stripeOptions.Value.Currency,
            now, now.AddMinutes(settings.CheckoutLifetimeMinutes));

        var frontend = settings.FrontendBaseUrl.TrimEnd('/');
        CheckoutSessionState session;
        try
        {
            session = await gateway.CreateCheckoutSessionAsync(new NewCheckoutSession(
                payment.Id,
                order.Id,
                order.CustomerEmail,
                payment.Currency,
                order.Lines.Select(l => new CheckoutLine(l.ProductName, l.UnitPrice, l.Quantity)).ToList(),
                // {CHECKOUT_SESSION_ID} is filled in by Stripe, so the shop knows which session to check.
                SuccessUrl: $"{frontend}/orders/{order.Id}?payment=success&session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl: $"{frontend}/orders/{order.Id}?payment=cancelled",
                payment.ExpiresAtUtc), ct);
        }
        catch (PaymentGatewayException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Stripe refused the request", detail: ex.Message);
        }

        payment.AttachCheckoutSession(session.SessionId, session.Url!);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Payment {PaymentId} started for order {OrderId}: {Amount} {Currency}",
            payment.Id, order.Id, payment.Amount, payment.Currency);
        return new StartCheckoutResponse(payment.Id, payment.CheckoutUrl!, payment.ExpiresAtUtc);
    }

    /// <summary>
    /// The customer is back from Stripe: ask Stripe how the session ended and record it now, instead of
    /// waiting for the webhook. Safe to call any number of times.
    /// </summary>
    [HttpPost("sessions/{sessionId}/refresh")]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PaymentDto>> Refresh(string sessionId, CancellationToken ct)
    {
        var known = await VisiblePayments().AnyAsync(p => p.StripeSessionId == sessionId, ct);
        if (!known)
        {
            return NotFound();
        }

        try
        {
            var session = await gateway.GetCheckoutSessionAsync(sessionId, ct);
            var payment = await workflow.ApplyCheckoutSessionAsync(session, ct);
            await db.SaveChangesAsync(ct);
            return PaymentDto.From(payment!);
        }
        catch (PaymentGatewayException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Stripe refused the request", detail: ex.Message);
        }
    }

    /// <summary>The payment attempts for an order (or the latest 50): your own, or everybody's if you are an admin.</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<PaymentDto>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<PaymentDto>> GetPayments([FromQuery] Guid? orderId, CancellationToken ct)
    {
        var query = VisiblePayments().AsNoTracking();
        if (orderId is not null)
        {
            query = query.Where(p => p.OrderId == orderId);
        }

        var payments = await query.OrderByDescending(p => p.CreatedAtUtc).Take(50).ToListAsync(ct);
        return payments.Select(PaymentDto.From);
    }

    private IQueryable<Payment> VisiblePayments()
    {
        if (User.IsAdmin())
        {
            return db.Payments;
        }

        var userId = User.GetUserId();
        return db.Payments.Where(p => p.CustomerId == userId);
    }
}
