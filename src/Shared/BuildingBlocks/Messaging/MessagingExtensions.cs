using System.Text.Json;
using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Messaging;

public static class MessagingExtensions
{
    /// <summary>
    /// Connects this service to RabbitMQ. <paramref name="queueName"/> is the service's own
    /// queue: every instance of the service shares it, so each message is handled by exactly
    /// one instance (competing consumers), while other services get their own copy in their
    /// own queue (publish/subscribe).
    /// </summary>
    public static MessagingBuilder AddMessaging(this IHostApplicationBuilder builder, string queueName)
    {
        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
        builder.Services.AddSingleton<RabbitMqConnection>();

        var registration = new MessagingRegistration(queueName);
        builder.Services.AddSingleton(registration);
        builder.Services.AddHostedService<RabbitMqConsumer>();

        // Degraded, not Unhealthy: without the broker the HTTP API still works and outgoing
        // events wait in the outbox, so the gateway should keep sending traffic here.
        builder.Services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", HealthStatus.Degraded, [ServiceDefaultsExtensions.ReadyTag]);

        return new MessagingBuilder(builder.Services, registration);
    }
}

public sealed class MessagingBuilder
{
    private readonly MessagingRegistration _registration;

    internal MessagingBuilder(IServiceCollection services, MessagingRegistration registration)
    {
        Services = services;
        _registration = registration;
    }

    public IServiceCollection Services { get; }

    /// <summary>Binds this service's queue to <paramref name="routingKey"/> and routes those messages to the handler.</summary>
    public MessagingBuilder Subscribe<TEvent, THandler>(string routingKey)
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        Services.TryAddScoped<THandler>();

        _registration.Add(new Subscription(routingKey, async (services, body, context, ct) =>
        {
            var message = JsonSerializer.Deserialize<TEvent>(body.Span, MessagingJson.Options)
                ?? throw new JsonException($"Message body for {routingKey} is empty.");
            await services.GetRequiredService<THandler>().HandleAsync(message, context, ct);
        }));

        return this;
    }

    /// <summary>Runs the background publisher for this service's outbox table.</summary>
    public MessagingBuilder AddOutbox<TDbContext>()
        where TDbContext : DbContext, IMessagingDbContext
    {
        Services.AddHostedService<OutboxProcessor<TDbContext>>();
        return this;
    }
}

internal sealed record Subscription(
    string RoutingKey,
    Func<IServiceProvider, ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> Handle);

internal sealed class MessagingRegistration(string queueName)
{
    public string QueueName { get; } = queueName;
    public Dictionary<string, Subscription> Subscriptions { get; } = [];

    public void Add(Subscription subscription)
    {
        if (!Subscriptions.TryAdd(subscription.RoutingKey, subscription))
        {
            throw new InvalidOperationException($"'{subscription.RoutingKey}' already has a handler.");
        }
    }
}

internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(connection.IsConnected
            ? HealthCheckResult.Healthy("Connected to RabbitMQ")
            : new HealthCheckResult(context.Registration.FailureStatus,
                "Not connected to RabbitMQ; reconnecting in the background"));
}
