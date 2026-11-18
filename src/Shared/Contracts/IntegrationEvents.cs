namespace Contracts;

/// <summary>
/// Routing keys on the topic exchange. Naming: "&lt;thing&gt;.&lt;what happened&gt;", in the past tense,
/// because an event is a fact that already happened, not a command ("reserve stock!").
/// </summary>
public static class RoutingKeys
{
    public const string OrderPlaced = "order.placed";
    public const string OrderConfirmed = "order.confirmed";
    public const string OrderRejected = "order.rejected";
    public const string OrderCancelled = "order.cancelled";
    public const string StockReserved = "stock.reserved";
    public const string StockReservationFailed = "stock.reservation-failed";
}

/// <summary>Published by Orders when a customer places an order (status Pending).</summary>
public record OrderPlaced(Guid OrderId, string CustomerEmail, decimal Total, IReadOnlyList<OrderPlacedLine> Lines);

public record OrderPlacedLine(Guid ProductId, int Quantity);

/// <summary>Published by Catalog when it has taken the stock for every line of the order.</summary>
public record StockReserved(Guid OrderId);

/// <summary>Published by Catalog when it could not reserve the stock (nothing is reserved then).</summary>
public record StockReservationFailed(Guid OrderId, string Reason);

/// <summary>Published by Orders once the order is final and successful.</summary>
public record OrderConfirmed(Guid OrderId, string CustomerEmail, decimal Total);

/// <summary>Published by Orders once the order is final and failed.</summary>
public record OrderRejected(Guid OrderId, string CustomerEmail, string Reason);

/// <summary>
/// Published by Orders when a customer (or an admin) cancels an order. Catalog reacts by giving back the
/// stock it reserved for this order: a compensating action, because there is no distributed transaction
/// to roll back. Notifications tells the customer.
/// </summary>
public record OrderCancelled(Guid OrderId, string CustomerEmail, decimal Total);
