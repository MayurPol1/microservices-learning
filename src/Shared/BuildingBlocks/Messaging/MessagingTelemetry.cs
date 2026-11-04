using System.Diagnostics;
using System.Text;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Distributed tracing across RabbitMQ.
/// <para>
/// Over HTTP, .NET passes the trace context for you in the W3C "traceparent" header
/// (format: 00-&lt;trace id&gt;-&lt;parent span id&gt;-&lt;flags&gt;). A message queue has no such magic,
/// so we do the same thing by hand:
/// </para>
/// <list type="number">
///   <item>When an event is written to the outbox, store the current traceparent next to it.</item>
///   <item>When the outbox publishes it, start a "publish" span under that parent and put the
///         span's traceparent into the RabbitMQ message headers.</item>
///   <item>The consumer reads the header and starts a "process" span with it as the parent.</item>
/// </list>
/// <para>
/// Result: one trace shows the HTTP request, the database calls, every hop through RabbitMQ
/// and every service that reacted, even though they ran seconds apart in different processes.
/// </para>
/// </summary>
public static class MessagingTelemetry
{
    /// <summary>Name to register with OpenTelemetry (<c>tracing.AddSource(...)</c>).</summary>
    public const string ActivitySourceName = "MicroShop.Messaging";

    private const string TraceParentHeader = "traceparent";
    private const string TraceStateHeader = "tracestate";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    /// <summary>The current trace context as a W3C traceparent string, or null outside a trace.</summary>
    internal static string? CurrentTraceParent() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

    internal static Activity? StartPublish(string routingKey, Guid messageId, string? storedTraceParent)
    {
        ActivityContext.TryParse(storedTraceParent, traceState: null, out var parent);
        var activity = Source.StartActivity($"{routingKey} publish", ActivityKind.Producer, parent);
        AddTags(activity, routingKey, messageId);
        return activity;
    }

    internal static Activity? StartProcess(string routingKey, Guid messageId, string queue, IDictionary<string, object?>? headers)
    {
        ActivityContext.TryParse(ReadHeader(headers, TraceParentHeader), ReadHeader(headers, TraceStateHeader), out var parent);
        var activity = Source.StartActivity($"{routingKey} process", ActivityKind.Consumer, parent);
        AddTags(activity, routingKey, messageId);
        activity?.SetTag("messaging.consumer.group.name", queue);
        return activity;
    }

    /// <summary>Writes the trace context into the message headers.</summary>
    internal static void Inject(Activity? activity, string? fallbackTraceParent, IDictionary<string, object?> headers)
    {
        var traceParent = activity?.Id ?? fallbackTraceParent;
        if (traceParent is not null)
        {
            headers[TraceParentHeader] = traceParent;
        }

        if (activity?.TraceStateString is { } traceState)
        {
            headers[TraceStateHeader] = traceState;
        }
    }

    private static void AddTags(Activity? activity, string routingKey, Guid messageId)
    {
        // Tag names follow the OpenTelemetry semantic conventions for messaging.
        activity?.SetTag("messaging.system", "rabbitmq")
            .SetTag("messaging.destination.name", routingKey)
            .SetTag("messaging.message.id", messageId.ToString());
    }

    // RabbitMQ delivers string headers as byte arrays.
    private static string? ReadHeader(IDictionary<string, object?>? headers, string key) =>
        headers is not null && headers.TryGetValue(key, out var value)
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => null,
            }
            : null;
}
