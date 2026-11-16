namespace Catalog.Api.Domain;

/// <summary>
/// A product for sale. The Catalog service is the single owner of product data:
/// name, price and stock live here and nowhere else. Other services may keep a
/// copy (Orders stores the price at the moment of purchase) but they never update it.
/// </summary>
public class Product
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1000;
    public const int CategoryMaxLength = 100;

    private Product() { } // for EF Core

    public Product(Guid id, string name, string? description, decimal price, int availableStock, string category)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(availableStock);

        Id = id;
        AvailableStock = availableStock;
        SetDetails(name, description, price, category);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public int AvailableStock { get; private set; }

    /// <summary>
    /// Optimistic concurrency token. EF Core adds "WHERE Version = @original" to every UPDATE,
    /// so if two requests change the same product at once, the second one fails instead of
    /// silently overwriting the first. That matters once several orders compete for stock.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Changes what the product *is* (name, description, price, category).
    /// Stock is deliberately not editable here: it only changes through <see cref="Restock"/> and
    /// <see cref="Reserve"/>. An admin typing "10" into a stock field could silently undo
    /// reservations that orders made a second earlier.
    /// </summary>
    public void UpdateDetails(string name, string? description, decimal price, string category)
    {
        SetDetails(name, description, price, category);
        Version++;
    }

    public void Restock(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        AvailableStock += quantity;
        Version++;
    }

    public bool CanReserve(int quantity) => quantity > 0 && AvailableStock >= quantity;

    /// <summary>Takes stock out of the available pool for an order.</summary>
    public void Reserve(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (AvailableStock < quantity)
        {
            throw new InvalidOperationException(
                $"Only {AvailableStock} x {Name} in stock, {quantity} requested.");
        }

        AvailableStock -= quantity;
        Version++;
    }

    private void SetDetails(string name, string? description, decimal price, string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentOutOfRangeException.ThrowIfNegative(price);

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Price = price;
        Category = category.Trim();
    }
}
