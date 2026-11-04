using System.ComponentModel.DataAnnotations;
using Catalog.Api.Domain;

namespace Catalog.Api.Models;

/// <summary>
/// What Catalog exposes over HTTP. The entity stays private: the API shape is a contract
/// with other services, the table shape is an implementation detail Catalog may change.
/// </summary>
public record ProductDto(Guid Id, string Name, string? Description, decimal Price, int AvailableStock)
{
    public static ProductDto From(Product p) => new(p.Id, p.Name, p.Description, p.Price, p.AvailableStock);
}

public record CreateProductRequest(
    [Required, StringLength(200)] string Name,
    [StringLength(1000)] string? Description,
    [Range(0, 1_000_000)] decimal Price,
    [Range(0, 1_000_000)] int InitialStock);

public record RestockRequest([Range(1, 1_000_000)] int Quantity);
