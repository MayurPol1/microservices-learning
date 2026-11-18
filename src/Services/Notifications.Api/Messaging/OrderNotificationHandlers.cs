using BuildingBlocks.Messaging;
using Contracts;

namespace Notifications.Api.Messaging;

/// <summary>
/// Turns order events into customer emails (logged, not really sent).
/// Orders didn't change at all when this service was added: Orders publishes facts to the
/// exchange and doesn't know who listens. Adding a new subscriber is just a new queue
/// bound to the same routing keys. That is the main benefit of publish/subscribe.
/// </summary>
public class OrderNotificationHandlers(NotificationStore store, ILogger<OrderNotificationHandlers> logger)
    : IIntegrationEventHandler<OrderPlaced>,
      IIntegrationEventHandler<OrderConfirmed>,
      IIntegrationEventHandler<OrderRejected>,
      IIntegrationEventHandler<OrderCancelled>,
      IIntegrationEventHandler<PaymentSucceeded>,
      IIntegrationEventHandler<PaymentRefunded>
{
    public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct) =>
        Send(context, message.OrderId, message.CustomerEmail,
            "We received your order",
            $"Order {message.OrderId} ({message.Total:0.00}) is being processed. We'll confirm it shortly.");

    public Task HandleAsync(OrderConfirmed message, MessageContext context, CancellationToken ct) =>
        Send(context, message.OrderId, message.CustomerEmail,
            "Your order is confirmed",
            $"Good news: the items in order {message.OrderId} are reserved for you. Pay {message.Total:0.00} to complete it.");

    public Task HandleAsync(OrderRejected message, MessageContext context, CancellationToken ct) =>
        Send(context, message.OrderId, message.CustomerEmail,
            "We couldn't complete your order",
            $"Sorry, order {message.OrderId} was rejected: {message.Reason}");

    public Task HandleAsync(OrderCancelled message, MessageContext context, CancellationToken ct) =>
        Send(context, message.OrderId, message.CustomerEmail,
            "Your order was cancelled",
            $"Order {message.OrderId} ({message.Total:0.00}) has been cancelled. If you already paid for it, the money is refunded.");

    public Task HandleAsync(PaymentSucceeded message, MessageContext context, CancellationToken ct) =>
        Send(context, message.OrderId, message.CustomerEmail,
            "Payment received",
            $"Thank you: we received {message.Amount:0.00} {message.Currency.ToUpperInvariant()} for order {message.OrderId}. It will ship soon.");

    public Task HandleAsync(PaymentRefunded message, MessageContext context, CancellationToken ct) =>
        Send(context, message.OrderId, message.CustomerEmail,
            "Your refund is on its way",
            $"We refunded {message.Amount:0.00} {message.Currency.ToUpperInvariant()} for cancelled order {message.OrderId}. Your bank shows it within a few days.");

    private Task Send(MessageContext context, Guid orderId, string to, string subject, string body)
    {
        if (store.IsHandled(context.MessageId))
        {
            logger.LogInformation("Message {MessageId} was already handled; not sending the email twice", context.MessageId);
            return Task.CompletedTask;
        }

        // A real service would call an email provider (SMTP, SendGrid, Amazon SES, ...) here.
        logger.LogInformation("EMAIL for order {OrderId}: \"{Subject}\"", orderId, subject);
        store.Record(context.MessageId, new Notification(Guid.NewGuid(), orderId, to, subject, body, DateTime.UtcNow));
        return Task.CompletedTask;
    }
}
