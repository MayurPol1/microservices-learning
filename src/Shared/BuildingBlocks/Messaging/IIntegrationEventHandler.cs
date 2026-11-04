using System.Text.Json;

namespace BuildingBlocks.Messaging;

/// <summary>Handles one type of integration event. One handler instance (and DbContext) per message.</summary>
public interface IIntegrationEventHandler<in TEvent>
{
    Task HandleAsync(TEvent message, MessageContext context, CancellationToken ct);
}

/// <summary>Metadata about the message being handled.</summary>
/// <param name="MessageId">Unique per published message; used to detect duplicates.</param>
/// <param name="RoutingKey">The event name, for example "order.placed".</param>
public sealed record MessageContext(Guid MessageId, string RoutingKey);

/// <summary>One JSON format for every message: camelCase, the ASP.NET Core web defaults.</summary>
public static class MessagingJson
{
    public static JsonSerializerOptions Options => JsonSerializerOptions.Web;
}
