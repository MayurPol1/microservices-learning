using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Security;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Clients;
using Orders.Api.Data;
using Orders.Api.Domain;
using Orders.Api.Models;

namespace Orders.Api.Controllers;

/// <summary>
/// Every endpoint needs a logged-in user ([Authorize] on the class: 401 without a valid token).
/// Customers only ever see and touch their own orders; admins see all of them.
/// </summary>
[ApiController]
[Route("api/orders")]
[Produces("application/json")]
[Authorize]
public class OrdersController(OrdersDbContext db, CatalogClient catalog, ILogger<OrdersController> logger) : ControllerBase
{
    /// <summary>
    /// Accepts an order. The response is 202 Accepted with status "Pending": the order is
    /// stored, but stock is reserved asynchronously. Poll GET /api/orders/{id} for the outcome.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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
        // The owner comes from the verified token, not from anything the client typed.
        var order = Order.Place(User.GetUserId(), User.GetEmail(), lines);

        db.Orders.Add(order);

        // Transactional outbox: the OrderPlaced event is saved in the SAME transaction as the
        // order. Either both are committed or neither is. The OutboxProcessor publishes it to
        // RabbitMQ a moment later. We never call RabbitMQ directly from a request.
        db.AddToOutbox(RoutingKeys.OrderPlaced, new OrderPlaced(
            order.Id,
            order.CustomerEmail,
            order.Total,
            order.Lines.Select(l => new OrderPlacedLine(l.ProductId, l.Quantity)).ToList()));

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Order {OrderId} placed with {LineCount} line(s), total {Total}; waiting for stock",
            order.Id, order.Lines.Count, order.Total);

        return AcceptedAtAction(nameof(GetOrder), new { id = order.Id }, OrderDto.From(order));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetOrder(Guid id, CancellationToken ct)
    {
        var order = await VisibleOrders().AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? NotFound() : OrderDto.From(order);
    }

    /// <summary>The 50 most recent orders: your own, or everybody's if you are an admin.</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<OrderDto>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<OrderDto>> GetOrders(CancellationToken ct)
    {
        var orders = await VisibleOrders().AsNoTracking()
            .Include(o => o.Lines)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);
        return orders.Select(OrderDto.From);
    }

    /// <summary>
    /// Cancels your order (admins: any order). The order is marked Cancelled and an order.cancelled event
    /// goes into the outbox in the same transaction; Catalog then gives back the reserved stock.
    /// Cancelling twice is harmless; a rejected order can't be cancelled (409).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> CancelOrder(Guid id, CancellationToken ct)
    {
        var order = await VisibleOrders().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status == OrderStatus.Rejected)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Order can't be cancelled",
                detail: "This order was rejected, so there is nothing to cancel.");
        }

        if (order.Cancel())
        {
            db.AddToOutbox(RoutingKeys.OrderCancelled, new OrderCancelled(order.Id, order.CustomerEmail, order.Total));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Order {OrderId} cancelled by {Role}", order.Id, User.IsAdmin() ? "admin" : "customer");
        }

        return OrderDto.From(order);
    }

    /// <summary>
    /// The orders the caller may see. Filtering in the query (rather than loading and then checking)
    /// means another customer's order simply isn't found: 404, which doesn't even reveal that it exists.
    /// </summary>
    private IQueryable<Order> VisibleOrders()
    {
        if (User.IsAdmin())
        {
            return db.Orders;
        }

        var userId = User.GetUserId();
        return db.Orders.Where(o => o.CustomerId == userId);
    }
}
