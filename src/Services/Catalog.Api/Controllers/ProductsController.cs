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
    /// <para>
    /// This endpoint's response shape (a plain array) is a contract Orders depends on, so paging
    /// was added as a separate endpoint (search) instead of changing this one.
    /// </para>
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

    /// <summary>One page of products, optionally filtered by category and/or name. Used by the web shop.</summary>
    [HttpGet("search")]
    [ProducesResponseType<PagedResult<ProductDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<ProductDto>> Search([FromQuery] ProductSearchQuery search, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search.Category))
        {
            var category = search.Category.Trim();
            query = query.Where(p => p.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(search.Q))
        {
            var text = search.Q.Trim();
            query = query.Where(p => p.Name.Contains(text));
        }

        var totalCount = await query.CountAsync(ct);
        var products = await query
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(ct);

        return new PagedResult<ProductDto>(products.Select(ProductDto.From).ToList(), search.Page, search.PageSize, totalCount);
    }

    /// <summary>The distinct categories, for the shop's filter.</summary>
    [HttpGet("categories")]
    [ProducesResponseType<IEnumerable<string>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<string>> GetCategories(CancellationToken ct) =>
        await db.Products.AsNoTracking().Select(p => p.Category).Distinct().OrderBy(c => c).ToListAsync(ct);

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
        var product = new Product(Guid.NewGuid(), request.Name, request.Description, request.Price, request.InitialStock, request.Category);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, ProductDto.From(product));
    }

    /// <summary>Changes name, description, price and category. Stock changes only through restock and orders.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return NotFound();
        }

        product.UpdateDetails(request.Name, request.Description, request.Price, request.Category);
        return await SaveOrConflictAsync(product, ct);
    }

    /// <summary>Adds stock (for example after a delivery from a supplier).</summary>
    [HttpPost("{id:guid}/restock")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Restock(Guid id, RestockRequest request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return NotFound();
        }

        product.Restock(request.Quantity);
        return await SaveOrConflictAsync(product, ct);
    }

    /// <summary>
    /// Removes a product. Orders that already contain it are unaffected: each order line keeps its own
    /// copy of the name and price (data ownership, lesson 1). An order placed for it afterwards is
    /// rejected by the stock reservation ("Product … no longer exists").
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return NotFound();
        }

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // The Version concurrency token makes SaveChanges fail if the product changed since we read it
    // (for example an order reserved stock in between). Tell the client to reload instead of failing with 500.
    private async Task<ActionResult<ProductDto>> SaveOrConflictAsync(Product product, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return ProductDto.From(product);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Product was changed",
                detail: "Someone else changed this product at the same time. Reload it and try again.");
        }
    }
}
