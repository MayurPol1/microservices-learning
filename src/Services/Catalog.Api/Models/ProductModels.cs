using System.ComponentModel.DataAnnotations;
using Catalog.Api.Domain;

namespace Catalog.Api.Models;

/// <summary>
/// What Catalog exposes over HTTP. The entity stays private: the API shape is a contract
/// with other services, the table shape is an implementation detail Catalog may change.
/// <para>
/// Category was added later. Adding a field is a backward-compatible change: Orders reads this
/// JSON with its own smaller record and simply ignores fields it doesn't know (tolerant reader).
/// </para>
/// </summary>
public record ProductDto(Guid Id, string Name, string? Description, decimal Price, int AvailableStock, string Category)
{
    public static ProductDto From(Product p) => new(p.Id, p.Name, p.Description, p.Price, p.AvailableStock, p.Category);
}

public record CreateProductRequest(
    [Required, StringLength(Product.NameMaxLength)] string Name,
    [StringLength(Product.DescriptionMaxLength)] string? Description,
    [Range(0, 1_000_000)] decimal Price,
    [Range(0, 1_000_000)] int InitialStock,
    [Required, StringLength(Product.CategoryMaxLength)] string Category);

/// <summary>Edits what a product is. Stock is not part of it: use restock (see Product.UpdateDetails).</summary>
public record UpdateProductRequest(
    [Required, StringLength(Product.NameMaxLength)] string Name,
    [StringLength(Product.DescriptionMaxLength)] string? Description,
    [Range(0, 1_000_000)] decimal Price,
    [Required, StringLength(Product.CategoryMaxLength)] string Category);

public record RestockRequest([Range(1, 1_000_000)] int Quantity);

/// <summary>Query string for GET api/products/search.</summary>
public class ProductSearchQuery
{
    /// <summary>Exact category, for example "Audio". Empty means all categories.</summary>
    [StringLength(Product.CategoryMaxLength)]
    public string? Category { get; init; }

    /// <summary>Text to look for in the product name.</summary>
    [StringLength(Product.NameMaxLength)]
    public string? Q { get; init; }

    [Range(1, 10_000)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 12;
}

/// <summary>One page of results plus the totals a client needs for paging controls.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
