using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Outbox;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Data;

namespace Orders.Api.Messaging;

/// <summary>
/// Last step of the order saga: Catalog has answered, so the order leaves "Pending".
/// Between the order being placed and this handler running, the system is temporarily
/// inconsistent (stock may be reserved while the order still says Pending). It becomes
/// consistent a moment later. That is eventual consistency.
/// </summary>
public class StockEventsHandler(OrdersDbContext db, ILogger<StockEventsHandler> logger)
    : IIntegrationEventHandler<StockReserved>, IIntegrationEventHandler<StockReservationFailed>
{
    public async Task HandleAsync(StockReserved message, MessageContext context, CancellationToken ct)
    {
        if (await IsDuplicateAsync(context, ct))
        {
            return;
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == message.OrderId, ct);
        if (order is null)
        {
            logger.LogWarning("StockReserved for unknown order {OrderId}; ignoring", message.OrderId);
        }
        else if (order.Confirm())
        {
            db.AddToOutbox(RoutingKeys.OrderConfirmed, new OrderConfirmed(order.Id, order.CustomerEmail, order.Total));
            logger.LogInformation("Order {OrderId} confirmed", order.Id);
        }
        else if (order.Status == Domain.OrderStatus.Cancelled)
        {
            // The customer cancelled while Catalog was reserving. Nothing to do here: Catalog handles
            // order.cancelled after order.placed (same queue, in order) and gives the stock back.
            logger.LogInformation("Stock reserved for order {OrderId}, which was already cancelled; Catalog will release it", order.Id);
        }

        db.MarkAsProcessed(context);
        await db.SaveChangesAsync(ct);
    }

    public async Task HandleAsync(StockReservationFailed message, MessageContext context, CancellationToken ct)
    {
        if (await IsDuplicateAsync(context, ct))
        {
            return;
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == message.OrderId, ct);
        if (order is null)
        {
            logger.LogWarning("StockReservationFailed for unknown order {OrderId}; ignoring", message.OrderId);
        }
        else if (order.Reject(message.Reason))
        {
            db.AddToOutbox(RoutingKeys.OrderRejected, new OrderRejected(order.Id, order.CustomerEmail, message.Reason));
            logger.LogInformation("Order {OrderId} rejected: {Reason}", order.Id, message.Reason);
        }

        db.MarkAsProcessed(context);
        await db.SaveChangesAsync(ct);
    }

    // Idempotency: a redelivered message must not confirm or reject the order a second time.
    private async Task<bool> IsDuplicateAsync(MessageContext context, CancellationToken ct)
    {
        if (!await db.IsAlreadyProcessedAsync(context, ct))
        {
            return false;
        }

        logger.LogInformation("Message {MessageId} ({RoutingKey}) was already handled; skipping duplicate",
            context.MessageId, context.RoutingKey);
        return true;
    }
}
