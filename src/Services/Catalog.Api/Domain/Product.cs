namespace Catalog.Api.Domain;

/// <summary>
/// A product for sale. The Catalog service is the single owner of product data:
/// name, price and stock live here and nowhere else. Other services may keep a
/// copy (Orders stores the price at the moment of purchase) but they never update it.
/// </summary>
public class Product
{
    private Product() { } // for EF Core

    public Product(Guid id, string name, string? description, decimal price, int availableStock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentOutOfRangeException.ThrowIfNegative(availableStock);

        Id = id;
        Name = name;
        Description = description;
        Price = price;
        AvailableStock = availableStock;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public int AvailableStock { get; private set; }

    /// <summary>
    /// Optimistic concurrency token. EF Core adds "WHERE Version = @original" to every UPDATE,
    /// so if two requests change the same product at once, the second one fails instead of
    /// silently overwriting the first. That matters once several orders compete for stock.
    /// </summary>
    public int Version { get; private set; }

    public void Restock(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        AvailableStock += quantity;
        Version++;
    }
}
