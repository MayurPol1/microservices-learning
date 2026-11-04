using Catalog.Api.Data;
using Catalog.Api.Domain;
using Catalog.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Controllers;

[ApiController]
[Route("api/products")]
[Produces("application/json")]
public class ProductsController(CatalogDbContext db) : ControllerBase
{
    /// <summary>
    /// Lists products. Pass ?ids=...&amp;ids=... to fetch several specific products in one call;
    /// the Orders service uses that to avoid one HTTP call per order line.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<ProductDto>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<ProductDto>> GetProducts([FromQuery] Guid[]? ids, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();
        if (ids is { Length: > 0 })
        {
            query = query.Where(p => ids.Contains(p.Id));
        }

        var products = await query.OrderBy(p => p.Name).ToListAsync(ct);
        return products.Select(ProductDto.From);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetProduct(Guid id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? NotFound() : ProductDto.From(product);
    }

    [HttpPost]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductRequest request, CancellationToken ct)
    {
        var product = new Product(Guid.NewGuid(), request.Name, request.Description, request.Price, request.InitialStock);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, ProductDto.From(product));
    }

    /// <summary>Adds stock (for example after a delivery from a supplier).</summary>
    [HttpPost("{id:guid}/restock")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> Restock(Guid id, RestockRequest request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return NotFound();
        }

        product.Restock(request.Quantity);
        await db.SaveChangesAsync(ct);
        return ProductDto.From(product);
    }
}
