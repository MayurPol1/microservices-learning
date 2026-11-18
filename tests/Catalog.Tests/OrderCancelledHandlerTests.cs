using BuildingBlocks.Messaging;
using Catalog.Api.Data;
using Catalog.Api.Domain;
using Catalog.Api.Messaging;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.Tests;

/// <summary>
/// The saga's compensating action: placing an order takes stock (OrderPlacedHandler), cancelling it gives
/// exactly that stock back (OrderCancelledHandler). Both handlers run against EF Core's in-memory database.
/// </summary>
public class OrderCancelledHandlerTests
{
    private static readonly Guid KeyboardId = Guid.NewGuid();
    private static readonly Guid MouseId = Guid.NewGuid();

    private static CatalogDbContext NewDatabase()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new CatalogDbContext(options);
        db.Products.AddRange(
            new Product(KeyboardId, "Keyboard", null, 80m, availableStock: 5, "Peripherals"),
            new Product(MouseId, "Mouse", null, 25m, availableStock: 1, "Peripherals"));
        db.SaveChanges();
        return db;
    }

    private static Task Place(CatalogDbContext db, Guid orderId, params (Guid ProductId, int Quantity)[] lines) =>
        new OrderPlacedHandler(db, NullLogger<OrderPlacedHandler>.Instance).HandleAsync(
            new OrderPlaced(orderId, "ada@example.com", 100m, lines.Select(l => new OrderPlacedLine(l.ProductId, l.Quantity)).ToList()),
            new MessageContext(Guid.NewGuid(), RoutingKeys.OrderPlaced),
            CancellationToken.None);

    private static Task Cancel(CatalogDbContext db, Guid orderId, MessageContext? context = null) =>
        new OrderCancelledHandler(db, NullLogger<OrderCancelledHandler>.Instance).HandleAsync(
            new OrderCancelled(orderId, "ada@example.com", 100m),
            context ?? new MessageContext(Guid.NewGuid(), RoutingKeys.OrderCancelled),
            CancellationToken.None);

    private static int Stock(CatalogDbContext db, Guid productId) => db.Products.Single(p => p.Id == productId).AvailableStock;

    [Fact]
    public async Task Placing_an_order_records_what_was_reserved()
    {
        await using var db = NewDatabase();
        var orderId = Guid.NewGuid();

        await Place(db, orderId, (KeyboardId, 2), (MouseId, 1));

        var reservation = await db.StockReservations.Include(r => r.Lines).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(orderId, reservation.OrderId);
        Assert.Equal(2, reservation.Lines.Single(l => l.ProductId == KeyboardId).Quantity);
        Assert.False(reservation.IsReleased);
    }

    [Fact]
    public async Task Cancelling_gives_back_exactly_the_reserved_stock()
    {
        await using var db = NewDatabase();
        var orderId = Guid.NewGuid();
        await Place(db, orderId, (KeyboardId, 2), (MouseId, 1));
        Assert.Equal(3, Stock(db, KeyboardId));

        await Cancel(db, orderId);

        Assert.Equal(5, Stock(db, KeyboardId));
        Assert.Equal(1, Stock(db, MouseId));
        Assert.True((await db.StockReservations.SingleAsync(TestContext.Current.CancellationToken)).IsReleased);
    }

    [Fact]
    public async Task Cancelling_an_order_that_got_no_stock_changes_nothing()
    {
        await using var db = NewDatabase();
        var orderId = Guid.NewGuid();
        await Place(db, orderId, (MouseId, 5)); // only 1 mouse: reservation fails, nothing taken

        await Cancel(db, orderId);

        Assert.Equal(1, Stock(db, MouseId));
        Assert.Empty(db.StockReservations);
    }

    [Fact]
    public async Task Stock_is_given_back_only_once_even_if_cancel_arrives_twice()
    {
        await using var db = NewDatabase();
        var orderId = Guid.NewGuid();
        await Place(db, orderId, (KeyboardId, 2));

        await Cancel(db, orderId, new MessageContext(Guid.NewGuid(), RoutingKeys.OrderCancelled)); // the event
        await Cancel(db, orderId, new MessageContext(Guid.NewGuid(), RoutingKeys.OrderCancelled)); // a second cancel event

        Assert.Equal(5, Stock(db, KeyboardId));
    }
}
