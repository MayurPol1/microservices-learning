namespace BuildingBlocks.Messaging;

/// <summary>Bound from the "RabbitMq" configuration section (RabbitMq__HostName etc. in Docker).</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Topic exchange every integration event is published to. Publishers only know this
    /// exchange and a routing key; they have no idea which queues (services) receive the event.
    /// </summary>
    public string Exchange { get; set; } = "microshop.events";

    /// <summary>Messages that fail too often are moved here instead of blocking the queue.</summary>
    public string DeadLetterExchange { get; set; } = "microshop.dead-letter";

    /// <summary>How many unacknowledged messages the broker may push to this consumer at once.</summary>
    public ushort PrefetchCount { get; set; } = 10;
}
