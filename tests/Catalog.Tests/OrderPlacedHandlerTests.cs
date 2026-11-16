using System.Text.Json;
using BuildingBlocks.Messaging;
using Catalog.Api.Data;
using Catalog.Api.Domain;
using Catalog.Api.Messaging;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.Tests;

/// <summary>
/// The handler's behaviour without RabbitMQ or MySQL: EF Core's in-memory provider stands in
/// for the database, and we call HandleAsync directly as the consumer would.
/// </summary>
public class OrderPlacedHandlerTests
{
    private static readonly Guid KeyboardId = Guid.NewGuid();
    private static readonly Guid MouseId = Guid.NewGuid();

    private static CatalogDbContext NewDatabase()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CatalogDbContext(options);
        db.Products.AddRange(
            new Product(KeyboardId, "Keyboard", null, 80m, availableStock: 5, "Peripherals"),
            new Product(MouseId, "Mouse", null, 25m, availableStock: 1, "Peripherals"));
        db.SaveChanges();
        return db;
    }

    private static Task Handle(CatalogDbContext db, OrderPlaced message, MessageContext context) =>
        new OrderPlacedHandler(db, NullLogger<OrderPlacedHandler>.Instance).HandleAsync(message, context, CancellationToken.None);

    private static OrderPlaced Order(params (Guid ProductId, int Quantity)[] lines) =>
        new(Guid.NewGuid(), "ada@example.com", 100m, lines.Select(l => new OrderPlacedLine(l.ProductId, l.Quantity)).ToList());

    private static MessageContext NewContext() => new(Guid.NewGuid(), RoutingKeys.OrderPlaced);

    [Fact]
    public async Task Enough_stock_reserves_it_and_queues_StockReserved()
    {
        await using var db = NewDatabase();
        var order = Order((KeyboardId, 2), (MouseId, 1));

        await Handle(db, order, NewContext());

        Assert.Equal(3, db.Products.Single(p => p.Id == KeyboardId).AvailableStock);
        Assert.Equal(0, db.Products.Single(p => p.Id == MouseId).AvailableStock);
        var outbox = Assert.Single(db.OutboxMessages);
        Assert.Equal(RoutingKeys.StockReserved, outbox.RoutingKey);
        Assert.Equal(order.OrderId, JsonSerializer.Deserialize<StockReserved>(outbox.Payload, MessagingJson.Options)!.OrderId);
    }

    [Fact]
    public async Task One_line_short_of_stock_reserves_nothing_and_queues_StockReservationFailed()
    {
        await using var db = NewDatabase();
        var order = Order((KeyboardId, 2), (MouseId, 2)); // only 1 mouse in stock

        await Handle(db, order, NewContext());

        // All or nothing: the keyboards were NOT taken either.
        Assert.Equal(5, db.Products.Single(p => p.Id == KeyboardId).AvailableStock);
        Assert.Equal(1, db.Products.Single(p => p.Id == MouseId).AvailableStock);
        var outbox = Assert.Single(db.OutboxMessages);
        Assert.Equal(RoutingKeys.StockReservationFailed, outbox.RoutingKey);
        Assert.Contains("Mouse", outbox.Payload);
    }

    [Fact]
    public async Task The_same_message_delivered_twice_is_handled_once()
    {
        await using var db = NewDatabase();
        var order = Order((KeyboardId, 1));
        var context = NewContext(); // same MessageId both times, as on a broker redelivery

        await Handle(db, order, context);
        await Handle(db, order, context);

        Assert.Equal(4, db.Products.Single(p => p.Id == KeyboardId).AvailableStock);
        Assert.Single(db.OutboxMessages);
        Assert.Single(db.ProcessedMessages);
    }
}
