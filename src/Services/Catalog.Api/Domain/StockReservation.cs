namespace Catalog.Api.Domain;

/// <summary>
/// Catalog's own record of the stock it took for one order. When the order is cancelled, Catalog gives
/// back exactly what it reserved, from its own data, without trusting anyone else's numbers. It's also how
/// Catalog knows there is nothing to give back (the reservation failed, or it was already released).
/// </summary>
public class StockReservation
{
    private readonly List<StockReservationLine> _lines = [];

    private StockReservation() { } // for EF Core

    public StockReservation(Guid orderId, IEnumerable<StockReservationLine> lines)
    {
        OrderId = orderId;
        ReservedAtUtc = DateTime.UtcNow;
        _lines.AddRange(lines);
    }

    /// <summary>One reservation per order, so the order id is the key.</summary>
    public Guid OrderId { get; private set; }
    public DateTime ReservedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public IReadOnlyList<StockReservationLine> Lines => _lines;

    public bool IsReleased => ReleasedAtUtc is not null;

    public void MarkReleased() => ReleasedAtUtc = DateTime.UtcNow;
}

public class StockReservationLine
{
    private StockReservationLine() { } // for EF Core

    public StockReservationLine(Guid productId, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        Id = Guid.NewGuid();
        ProductId = productId;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
}
