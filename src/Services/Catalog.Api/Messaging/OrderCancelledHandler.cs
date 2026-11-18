using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Outbox;
using Catalog.Api.Data;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Messaging;

/// <summary>
/// Reacts to "order.cancelled" by putting back the stock reserved for that order: the compensating
/// action of the order saga. There is no distributed transaction to roll back, so each service undoes
/// its own part with a new local transaction.
/// <para>
/// Ordering matters and is guaranteed here: Orders writes order.placed before order.cancelled to its
/// outbox, the outbox publishes in that order, and both land in this service's "catalog" queue, which
/// is consumed one message at a time. So by the time this runs, OrderPlacedHandler has already either
/// reserved the stock (there is a reservation to release) or failed to (there is none).
/// </para>
/// </summary>
public class OrderCancelledHandler(CatalogDbContext db, ILogger<OrderCancelledHandler> logger)
    : IIntegrationEventHandler<OrderCancelled>
{
    public async Task HandleAsync(OrderCancelled message, MessageContext context, CancellationToken ct)
    {
        if (await db.IsAlreadyProcessedAsync(context, ct))
        {
            logger.LogInformation("Message {MessageId} was already handled; skipping duplicate", context.MessageId);
            return;
        }

        var reservation = await db.StockReservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.OrderId == message.OrderId, ct);

        if (reservation is null)
        {
            // The reservation failed (order rejected), so no stock was ever taken.
            logger.LogInformation("Order {OrderId} cancelled; no stock was reserved for it, nothing to release", message.OrderId);
        }
        else if (reservation.IsReleased)
        {
            logger.LogInformation("Stock for order {OrderId} was already released", message.OrderId);
        }
        else
        {
            var productIds = reservation.Lines.Select(l => l.ProductId).ToList();
            var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

            foreach (var line in reservation.Lines)
            {
                if (products.TryGetValue(line.ProductId, out var product))
                {
                    product.Restock(line.Quantity);
                }
                else
                {
                    logger.LogWarning("Product {ProductId} no longer exists; can't give back {Quantity}", line.ProductId, line.Quantity);
                }
            }

            reservation.MarkReleased();
            logger.LogInformation("Released stock for cancelled order {OrderId} ({Lines} line(s))", message.OrderId, reservation.Lines.Count);
        }

        // One transaction: stock given back + reservation marked released + message marked handled.
        db.MarkAsProcessed(context);
        await db.SaveChangesAsync(ct);
    }
}
