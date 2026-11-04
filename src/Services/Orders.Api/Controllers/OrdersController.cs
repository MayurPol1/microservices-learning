using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Clients;
using Orders.Api.Data;
using Orders.Api.Domain;
using Orders.Api.Models;

namespace Orders.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public class OrdersController(OrdersDbContext db, CatalogClient catalog, ILogger<OrdersController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderDto>> PlaceOrder(PlaceOrderRequest request, CancellationToken ct)
    {
        // Merge duplicate lines for the same product.
        var wanted = request.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        // SYNCHRONOUS call: we need Catalog's current name and price before we can accept the order.
        // The price of this: if Catalog is down, placing an order fails too (temporal coupling).
        IReadOnlyList<CatalogProduct> products;
        try
        {
            products = await catalog.GetProductsAsync(wanted.Keys, ct);
        }
        catch (CatalogUnavailableException ex)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable", detail: ex.Message);
        }

        var unknownIds = wanted.Keys.Except(products.Select(p => p.Id)).ToList();
        if (unknownIds.Count > 0)
        {
            ModelState.AddModelError(nameof(request.Items), $"Unknown product id(s): {string.Join(", ", unknownIds)}");
            return ValidationProblem();
        }

        // Snapshot name and price into the order lines.
        var lines = products.Select(p => new OrderLine(p.Id, p.Name, p.Price, wanted[p.Id]));
        var order = Order.Place(request.CustomerEmail, lines);

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Order {OrderId} placed with {LineCount} line(s), total {Total}",
            order.Id, order.Lines.Count, order.Total);

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, OrderDto.From(order));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetOrder(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? NotFound() : OrderDto.From(order);
    }

    /// <summary>The 50 most recent orders.</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<OrderDto>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<OrderDto>> GetOrders(CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);
        return orders.Select(OrderDto.From);
    }
}
