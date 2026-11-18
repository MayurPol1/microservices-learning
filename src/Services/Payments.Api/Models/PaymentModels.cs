using Payments.Api.Domain;

namespace Payments.Api.Models;

/// <summary>Only the order id. The amount comes from Orders, never from the browser: otherwise anyone could pay $0.01.</summary>
public record StartCheckoutRequest(Guid OrderId);

/// <summary>Send the browser to CheckoutUrl: Stripe's hosted payment page.</summary>
public record StartCheckoutResponse(Guid PaymentId, string CheckoutUrl, DateTime ExpiresAtUtc);

public record PaymentDto(
    Guid Id,
    Guid OrderId,
    string Status,
    decimal Amount,
    string Currency,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? PaidAtUtc,
    DateTime? RefundedAtUtc,
    DateTime? ClosedAtUtc)
{
    public static PaymentDto From(Payment p) => new(
        p.Id, p.OrderId, p.Status.ToString(), p.Amount, p.Currency, p.CreatedAtUtc, p.ExpiresAtUtc, p.PaidAtUtc, p.RefundedAtUtc, p.ClosedAtUtc);
}
