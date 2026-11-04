using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Background loop: every second, read unsent outbox rows and publish them to RabbitMQ.
/// If RabbitMQ is down, rows simply wait in the table and go out when it's back, so the
/// service keeps accepting requests during a broker outage.
/// </summary>
internal sealed class OutboxProcessor<TDbContext>(
    IServiceScopeFactory scopeFactory,
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    ILogger<OutboxProcessor<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext, IMessagingDbContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IChannel? channel = null;
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    channel ??= await OpenPublisherChannelAsync(stoppingToken);
                    await PublishPendingAsync(channel, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Outbox publishing failed, will retry: {Error}", ex.Message);
                    if (channel is { IsOpen: false })
                    {
                        await channel.DisposeAsync();
                        channel = null;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            if (channel is not null)
            {
                await channel.DisposeAsync();
            }
        }
    }

    private async Task<IChannel> OpenPublisherChannelAsync(CancellationToken ct)
    {
        var conn = await connection.GetConnectionAsync(ct);

        // Publisher confirms: BasicPublishAsync completes only after the broker has taken
        // responsibility for the message, and throws if it refuses it. Only then do we mark
        // the outbox row as sent.
        var channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);

        await RabbitMqTopology.DeclareExchangesAsync(channel, options.Value, ct);
        return channel;
    }

    private async Task PublishPendingAsync(IChannel channel, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in pending)
        {
            // Continue the trace of the request that created the event (see MessagingTelemetry).
            using var activity = MessagingTelemetry.StartPublish(message.RoutingKey, message.Id, message.TraceParent);

            var headers = new Dictionary<string, object?>();
            MessagingTelemetry.Inject(activity, message.TraceParent, headers);

            var properties = new BasicProperties
            {
                MessageId = message.Id.ToString(),
                Type = message.RoutingKey,
                ContentType = "application/json",
                // Written to disk by the broker, so it survives a RabbitMQ restart (the queue is durable too).
                DeliveryMode = DeliveryModes.Persistent,
                Headers = headers,
            };

            await channel.BasicPublishAsync(
                exchange: options.Value.Exchange,
                routingKey: message.RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken: ct);

            // If the process dies right here, the row is still unsent and will be published
            // again on restart. That's why delivery is "at least once" and consumers must be idempotent.
            message.MarkProcessed();
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Published {RoutingKey} message {MessageId}", message.RoutingKey, message.Id);
        }
    }
}
