namespace Orders.Api.Domain;

public enum OrderStatus
{
    /// <summary>Accepted, waiting for Catalog to reserve the stock.</summary>
    Pending,
    Confirmed,
    Rejected,

    /// <summary>Cancelled by the customer or an admin; any reserved stock is given back by Catalog.</summary>
    Cancelled,
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

    /// <summary>
    /// The owner: the user id ("sub" claim) from the JWT of whoever placed the order. Orders doesn't
    /// have a users table; it trusts the signed token, the same way a hotel trusts a passport.
    /// </summary>
    public Guid CustomerId { get; private set; }

    public string CustomerEmail { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public decimal Total { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;

    public static Order Place(Guid customerId, string customerEmail, IEnumerable<OrderLine> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerEmail);
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("An order needs an owner.", nameof(customerId));
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
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

    /// <summary>
    /// Returns false (nothing changes) if the order was already confirmed, a harmless duplicate message,
    /// or already cancelled: the customer cancelled while Catalog was still reserving the stock, and
    /// Catalog gives that stock back when it handles order.cancelled.
    /// </summary>
    public bool Confirm()
    {
        if (Status is OrderStatus.Confirmed or OrderStatus.Cancelled)
        {
            return false;
        }

        EnsurePending();
        Status = OrderStatus.Confirmed;
        CompletedAtUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>Returns false if the order was already rejected or cancelled.</summary>
    public bool Reject(string reason)
    {
        if (Status is OrderStatus.Rejected or OrderStatus.Cancelled)
        {
            return false;
        }

        EnsurePending();
        Status = OrderStatus.Rejected;
        RejectionReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Cancels a Pending or Confirmed order. Returns false if it was already cancelled (cancelling twice is
    /// harmless). A Rejected order can't be cancelled: it never got any stock, there is nothing to undo.
    /// </summary>
    public bool Cancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return false;
        }

        if (Status == OrderStatus.Rejected)
        {
            throw new InvalidOperationException($"Order {Id} was rejected, so there is nothing to cancel.");
        }

        Status = OrderStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
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
