using System.ComponentModel.DataAnnotations;
using Orders.Api.Domain;

namespace Orders.Api.Models;

public record PlaceOrderRequest(
    [Required, EmailAddress] string CustomerEmail,
    [Required, MinLength(1)] List<OrderItemRequest> Items);

public record OrderItemRequest(Guid ProductId, [Range(1, 100)] int Quantity);

public record OrderLineDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public record OrderDto(
    Guid Id,
    string CustomerEmail,
    string Status,
    decimal Total,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<OrderLineDto> Lines)
{
    public static OrderDto From(Order o) => new(
        o.Id,
        o.CustomerEmail,
        o.Status.ToString(),
        o.Total,
        o.RejectionReason,
        o.CreatedAtUtc,
        o.CompletedAtUtc,
        o.Lines.Select(l => new OrderLineDto(l.ProductId, l.ProductName, l.UnitPrice, l.Quantity, l.LineTotal)).ToList());
}
