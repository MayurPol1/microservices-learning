using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Outbox;
using Catalog.Api.Data;
using Catalog.Api.Domain;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Messaging;

/// <summary>
/// Reacts to "order.placed": tries to reserve stock for every line, then announces the result
/// with "stock.reserved" or "stock.reservation-failed". Catalog never calls Orders back
/// directly. It publishes a fact, and whoever cares (Orders) reacts to it.
/// This chain of events is a choreographed saga: a business process spread over
/// several services, each doing its local transaction and publishing what happened.
/// </summary>
public class OrderPlacedHandler(CatalogDbContext db, ILogger<OrderPlacedHandler> logger)
    : IIntegrationEventHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
    {
        // Idempotency: the same message can be delivered twice (at-least-once delivery).
        if (await db.IsAlreadyProcessedAsync(context, ct))
        {
            logger.LogInformation("Message {MessageId} was already handled; skipping duplicate", context.MessageId);
            return;
        }

        var wanted = message.Lines
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var productIds = wanted.Keys.ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        // All or nothing: check every line first, reserve only if all of them fit.
        var problem = FindProblem(wanted, products);
        if (problem is null)
        {
            foreach (var (productId, quantity) in wanted)
            {
                products[productId].Reserve(quantity);
            }

            // Remember what was taken for this order, so a cancellation can give back exactly this.
            db.StockReservations.Add(new StockReservation(
                message.OrderId,
                wanted.Select(w => new StockReservationLine(w.Key, w.Value))));

            db.AddToOutbox(RoutingKeys.StockReserved, new StockReserved(message.OrderId));
            logger.LogInformation("Reserved stock for order {OrderId}", message.OrderId);
        }
        else
        {
            db.AddToOutbox(RoutingKeys.StockReservationFailed, new StockReservationFailed(message.OrderId, problem));
            logger.LogWarning("Could not reserve stock for order {OrderId}: {Reason}", message.OrderId, problem);
        }

        // ONE SaveChanges = ONE transaction for: stock change + outgoing event + "handled" marker.
        // If two orders race for the same product, the Version concurrency token makes one of
        // them fail here; the consumer retries it with fresh data.
        db.MarkAsProcessed(context);
        await db.SaveChangesAsync(ct);
    }

    private static string? FindProblem(Dictionary<Guid, int> wanted, Dictionary<Guid, Product> products)
    {
        foreach (var (productId, quantity) in wanted)
        {
            if (!products.TryGetValue(productId, out var product))
            {
                return $"Product {productId} no longer exists.";
            }

            if (!product.CanReserve(quantity))
            {
                return $"Not enough stock for {product.Name}: {product.AvailableStock} available, {quantity} requested.";
            }
        }

        return null;
    }
}
