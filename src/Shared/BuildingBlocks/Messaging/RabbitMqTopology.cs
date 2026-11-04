using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Declares exchanges and queues. Declarations are idempotent: declaring something that
/// already exists with the same settings does nothing, so every service can safely declare
/// what it needs at startup.
/// <code>
///   publisher --(routing key "order.placed")--> [microshop.events: topic exchange]
///                                                  |  binding "order.placed"   |  binding "order.placed"
///                                                  v                           v
///                                            [queue: catalog]          [queue: notifications]
///                                                  | rejected 3x
///                                                  v
///                         [microshop.dead-letter] --> [queue: catalog.dead-letter]
/// </code>
/// </summary>
internal static class RabbitMqTopology
{
    public static async Task DeclareExchangesAsync(IChannel channel, RabbitMqOptions options, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(options.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
    }

    /// <summary>Declares one service's queue, its bindings, and its dead-letter queue.</summary>
    public static async Task DeclareServiceQueueAsync(
        IChannel channel, RabbitMqOptions options, string queue, IEnumerable<string> routingKeys, CancellationToken ct)
    {
        await DeclareExchangesAsync(channel, options, ct);

        var deadLetterQueue = $"{queue}.dead-letter";
        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(deadLetterQueue, options.DeadLetterExchange, routingKey: queue, cancellationToken: ct);

        // durable: the queue survives a broker restart. The arguments tell RabbitMQ where to
        // move messages we reject with requeue=false.
        var arguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = options.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = queue,
        };
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments, cancellationToken: ct);

        foreach (var routingKey in routingKeys)
        {
            await channel.QueueBindAsync(queue, options.Exchange, routingKey, cancellationToken: ct);
        }
    }
}
