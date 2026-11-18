using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Outbox;
using Contracts;
using Payments.Api.Application;
using Payments.Api.Data;

namespace Payments.Api.Messaging;

/// <summary>
/// Payments' part of the cancel saga, next to Catalog's stock release: close the Stripe page if the order wasn't
/// paid yet, or refund it if it was. Both are compensating actions.
/// </summary>
public class OrderCancelledHandler(PaymentsDbContext db, PaymentWorkflow workflow, ILogger<OrderCancelledHandler> logger)
    : IIntegrationEventHandler<OrderCancelled>
{
    public async Task HandleAsync(OrderCancelled message, MessageContext context, CancellationToken ct)
    {
        if (await db.IsAlreadyProcessedAsync(context, ct))
        {
            logger.LogInformation("Message {MessageId} was already handled; skipping duplicate", context.MessageId);
            return;
        }

        await workflow.OrderCancelledAsync(message.OrderId, ct);

        // One transaction: payment changes + payment.refunded event (if any) + message marked handled.
        db.MarkAsProcessed(context);
        await db.SaveChangesAsync(ct);
    }
}
