namespace Orders.Api.Domain;

public enum OrderStatus
{
    /// <summary>Accepted, waiting for Catalog to reserve the stock.</summary>
    Pending,
    Confirmed,
    Rejected,
}

/// <summary>
/// An order and its lines. The Orders service owns this data.
/// Each line keeps a copy of the product name and price as they were when the order was placed:
/// if Catalog changes the price tomorrow, yesterday's order must not change.
/// </summary>
public class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order() { } // for EF Core

    public Guid Id { get; private set; }
    public string CustomerEmail { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public decimal Total { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;

    public static Order Place(string customerEmail, IEnumerable<OrderLine> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerEmail);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerEmail = customerEmail,
            Status = OrderStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        };
        order._lines.AddRange(lines);

        if (order._lines.Count == 0)
        {
            throw new ArgumentException("An order needs at least one line.", nameof(lines));
        }

        order.Total = order._lines.Sum(l => l.LineTotal);
        return order;
    }

    /// <summary>Returns false if the order was already confirmed (a duplicate message is harmless).</summary>
    public bool Confirm()
    {
        if (Status == OrderStatus.Confirmed)
        {
            return false;
        }

        EnsurePending();
        Status = OrderStatus.Confirmed;
        CompletedAtUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>Returns false if the order was already rejected.</summary>
    public bool Reject(string reason)
    {
        if (Status == OrderStatus.Rejected)
        {
            return false;
        }

        EnsurePending();
        Status = OrderStatus.Rejected;
        RejectionReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
        return true;
    }

    private void EnsurePending()
    {
        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"Order {Id} is already {Status} and can't change any more.");
        }
    }
}

public class OrderLine
{
    private OrderLine() { } // for EF Core

    public OrderLine(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        Id = Guid.NewGuid();
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal => UnitPrice * Quantity;
}
