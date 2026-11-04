using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Messaging.Outbox;

/// <summary>A service's DbContext implements this to get an outbox and an inbox table.</summary>
public interface IMessagingDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<ProcessedMessage> ProcessedMessages { get; }
}

public static class MessagingDbContextExtensions
{
    /// <summary>Call from OnModelCreating to map the outbox_messages and processed_messages tables.</summary>
    public static ModelBuilder AddMessagingTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("outbox_messages");
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.RoutingKey).HasMaxLength(100).IsRequired();
            outbox.Property(m => m.Payload).IsRequired();
            outbox.HasIndex(m => m.ProcessedOnUtc);
        });

        modelBuilder.Entity<ProcessedMessage>(inbox =>
        {
            inbox.ToTable("processed_messages");
            inbox.HasKey(m => m.Id); // primary key = message id, so a duplicate insert fails
            inbox.Property(m => m.RoutingKey).HasMaxLength(100).IsRequired();
        });

        return modelBuilder;
    }

    /// <summary>
    /// Queues an event for publishing. Nothing is sent yet: the row is written by your next
    /// SaveChangesAsync, in the same transaction as your business changes.
    /// </summary>
    public static void AddToOutbox<TEvent>(this IMessagingDbContext db, string routingKey, TEvent message)
        where TEvent : notnull
    {
        var payload = JsonSerializer.Serialize(message, MessagingJson.Options);
        db.OutboxMessages.Add(OutboxMessage.Create(routingKey, payload));
    }

    public static Task<bool> IsAlreadyProcessedAsync(this IMessagingDbContext db, MessageContext context, CancellationToken ct)
        => db.ProcessedMessages.AnyAsync(m => m.Id == context.MessageId, ct);

    /// <summary>Records the message as handled; saved by the same SaveChangesAsync as the handler's work.</summary>
    public static void MarkAsProcessed(this IMessagingDbContext db, MessageContext context)
        => db.ProcessedMessages.Add(new ProcessedMessage(context.MessageId, context.RoutingKey));
}
