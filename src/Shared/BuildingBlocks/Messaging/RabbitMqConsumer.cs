using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Reads this service's queue and dispatches each message to its handler.
/// A message is acknowledged only after the handler succeeded; until then RabbitMQ keeps it,
/// and if this process dies it delivers the message again.
/// </summary>
internal sealed class RabbitMqConsumer(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    MessagingRegistration registration,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqConsumer> logger) : BackgroundService
{
    private const int MaxAttempts = 3;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (registration.Subscriptions.Count == 0)
        {
            return; // this service only publishes
        }

        var conn = await connection.GetConnectionAsync(stoppingToken);
        _channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);

        await RabbitMqTopology.DeclareServiceQueueAsync(
            _channel, options.Value, registration.QueueName, registration.Subscriptions.Keys, stoppingToken);

        // Back-pressure: the broker sends at most PrefetchCount unacknowledged messages at a time.
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: options.Value.PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(_channel, delivery, stoppingToken);

        // autoAck: false. We ack manually after the handler commits its transaction.
        await _channel.BasicConsumeAsync(registration.QueueName, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("Consuming queue '{Queue}' bound to {RoutingKeys}",
            registration.QueueName, string.Join(", ", registration.Subscriptions.Keys));
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var routingKey = delivery.RoutingKey;

        if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId))
        {
            logger.LogError("Message on {RoutingKey} has no valid MessageId; dead-lettering it", routingKey);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        if (!registration.Subscriptions.TryGetValue(routingKey, out var subscription))
        {
            // Tolerant consumer: an event type we don't know is not an error.
            logger.LogWarning("No handler for {RoutingKey}; acknowledging and ignoring it", routingKey);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        var context = new MessageContext(messageId, routingKey);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // A fresh DI scope per attempt: new DbContext, no state left over from a failed try.
                await using var scope = scopeFactory.CreateAsyncScope();
                await subscription.Handle(scope.ServiceProvider, delivery.Body, context, stoppingToken);

                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                logger.LogInformation("Handled {RoutingKey} message {MessageId}", routingKey, messageId);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down. No ack, so RabbitMQ will deliver the message again later.
                return;
            }
            catch (JsonException ex)
            {
                // A "poison message": retrying can never succeed.
                logger.LogError(ex, "Cannot read {RoutingKey} message {MessageId}; dead-lettering it", routingKey, messageId);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning("Handling {RoutingKey} message {MessageId} failed (attempt {Attempt}/{Max}): {Error}",
                    routingKey, messageId, attempt, MaxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), stoppingToken);
            }
            catch (Exception ex)
            {
                // Park it in the dead-letter queue so it doesn't block the messages behind it.
                // Someone can inspect it in the RabbitMQ UI and re-publish it after a fix.
                logger.LogError(ex, "Giving up on {RoutingKey} message {MessageId} after {Max} attempts; dead-lettering it",
                    routingKey, messageId, MaxAttempts);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                return;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }
    }
}
