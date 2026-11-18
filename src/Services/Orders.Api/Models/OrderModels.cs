using System.ComponentModel.DataAnnotations;
using Orders.Api.Domain;

namespace Orders.Api.Models;

/// <summary>
/// Only the items. Who is ordering (id and email) comes from the JWT, never from the body:
/// otherwise anyone could place an order in someone else's name.
/// </summary>
public record PlaceOrderRequest([Required, MinLength(1)] List<OrderItemRequest> Items);

public record OrderItemRequest(Guid ProductId, [Range(1, 100)] int Quantity);

public record OrderLineDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public record OrderDto(
    Guid Id,
    Guid CustomerId,
    string CustomerEmail,
    string Status,
    decimal Total,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    DateTime? PaidAtUtc,
    IReadOnlyList<OrderLineDto> Lines)
{
    public static OrderDto From(Order o) => new(
        o.Id,
        o.CustomerId,
        o.CustomerEmail,
        o.Status.ToString(),
        o.Total,
        o.RejectionReason,
        o.CreatedAtUtc,
        o.CompletedAtUtc,
        o.CancelledAtUtc,
        o.PaidAtUtc,
        o.Lines.Select(l => new OrderLineDto(l.ProductId, l.ProductName, l.UnitPrice, l.Quantity, l.LineTotal)).ToList());
}
