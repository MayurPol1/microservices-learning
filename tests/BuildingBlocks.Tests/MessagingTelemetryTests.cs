using System.Diagnostics;
using System.Text;
using BuildingBlocks.Messaging;

namespace BuildingBlocks.Tests;

/// <summary>
/// Shows, without a broker, how one trace survives an asynchronous hop:
/// request -> outbox row -> RabbitMQ headers -> consumer.
/// </summary>
public class MessagingTelemetryTests : IDisposable
{
    private readonly ActivitySource _testSource = new("Tests.Http");
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = source => source.Name is "Tests.Http" or MessagingTelemetry.ActivitySourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public MessagingTelemetryTests() => ActivitySource.AddActivityListener(_listener);

    [Fact]
    public void Trace_context_survives_the_trip_through_the_outbox_and_message_headers()
    {
        // 1. An HTTP request is running. The outbox row stores its traceparent.
        string? storedTraceParent;
        ActivityTraceId requestTraceId;
        using (var request = _testSource.StartActivity("POST /api/orders"))
        {
            requestTraceId = request!.TraceId;
            storedTraceParent = MessagingTelemetry.CurrentTraceParent();
        }

        // 2. Later, the outbox loop publishes. The request activity is long gone.
        var headers = new Dictionary<string, object?>();
        using (var publish = MessagingTelemetry.StartPublish("order.placed", Guid.NewGuid(), storedTraceParent))
        {
            Assert.Equal(requestTraceId, publish!.TraceId);
            Assert.Equal(ActivityKind.Producer, publish.Kind);
            MessagingTelemetry.Inject(publish, storedTraceParent, headers);
        }

        // 3. RabbitMQ hands string headers to the consumer as byte arrays.
        var received = headers.ToDictionary(h => h.Key, h => (object?)Encoding.UTF8.GetBytes((string)h.Value!));
        using var process = MessagingTelemetry.StartProcess("order.placed", Guid.NewGuid(), "catalog", received);

        Assert.Equal(requestTraceId, process!.TraceId);
        Assert.Equal(ActivityKind.Consumer, process.Kind);
    }

    [Fact]
    public void A_message_without_trace_headers_starts_a_new_trace()
    {
        using var process = MessagingTelemetry.StartProcess("order.placed", Guid.NewGuid(), "catalog", headers: null);

        Assert.NotNull(process);
        Assert.Equal(default, process.ParentSpanId);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _testSource.Dispose();
    }
}
