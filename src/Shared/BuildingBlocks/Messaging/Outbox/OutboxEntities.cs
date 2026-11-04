namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// An event waiting to be published, stored in the service's own database.
/// <para>
/// Why: "save the order" and "publish OrderPlaced" go to two different systems (MySQL and
/// RabbitMQ). No transaction covers both, so a crash between them leaves an order nobody
/// hears about, or an event for an order that was never saved (the "dual write" problem).
/// The outbox turns it into one local transaction: the order row and the outbox row are
/// committed together, and <see cref="OutboxProcessor{TDbContext}"/> publishes the row afterwards.
/// </para>
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { } // for EF Core

    public Guid Id { get; private set; }
    public string RoutingKey { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTime OccurredOnUtc { get; private set; }
    public DateTime? ProcessedOnUtc { get; private set; }

    public static OutboxMessage Create(string routingKey, string payload) => new()
    {
        Id = Guid.NewGuid(),
        RoutingKey = routingKey,
        Payload = payload,
        OccurredOnUtc = DateTime.UtcNow,
    };

    public void MarkProcessed() => ProcessedOnUtc = DateTime.UtcNow;
}

/// <summary>
/// A message this service has already handled (an "inbox" record).
/// <para>
/// Why: RabbitMQ guarantees at-least-once delivery, not exactly-once. If a consumer crashes
/// after doing the work but before acknowledging, the broker delivers the message again.
/// Storing the message id in the same transaction as the work lets the handler recognise
/// and skip the duplicate. Handlers that behave this way are called idempotent.
/// </para>
/// </summary>
public sealed class ProcessedMessage
{
    private ProcessedMessage() { } // for EF Core

    public ProcessedMessage(Guid id, string routingKey)
    {
        Id = id;
        RoutingKey = routingKey;
        ProcessedOnUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string RoutingKey { get; private set; } = string.Empty;
    public DateTime ProcessedOnUtc { get; private set; }
}
