using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Outbox;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Data;

namespace Orders.Api.Messaging;

/// <summary>
/// Payments tells Orders that an order was paid. Orders never talks to Stripe and never sees card data:
/// it only learns the outcome, as an event.
/// </summary>
public class PaymentEventsHandler(OrdersDbContext db, ILogger<PaymentEventsHandler> logger)
    : IIntegrationEventHandler<PaymentSucceeded>
{
    public async Task HandleAsync(PaymentSucceeded message, MessageContext context, CancellationToken ct)
    {
        if (await db.IsAlreadyProcessedAsync(context, ct))
        {
            logger.LogInformation("Message {MessageId} was already handled; skipping duplicate", context.MessageId);
            return;
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == message.OrderId, ct);
        if (order is null)
        {
            logger.LogWarning("PaymentSucceeded for unknown order {OrderId}; ignoring", message.OrderId);
        }
        else if (order.MarkPaid())
        {
            logger.LogInformation("Order {OrderId} paid ({Amount} {Currency})", order.Id, message.Amount, message.Currency);
        }
        else if (order.Status == Domain.OrderStatus.Cancelled)
        {
            // Paid at the same moment it was cancelled. Payments knows about the cancellation too and refunds it.
            logger.LogInformation("Payment arrived for order {OrderId}, which was already cancelled; Payments refunds it", order.Id);
        }

        db.MarkAsProcessed(context);
        await db.SaveChangesAsync(ct);
    }
}
