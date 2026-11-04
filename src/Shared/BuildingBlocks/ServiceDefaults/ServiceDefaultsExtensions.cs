using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.ServiceDefaults;

/// <summary>
/// Cross-cutting setup that every service in the system gets. Each service calls
/// <c>builder.AddServiceDefaults()</c> and <c>app.MapDefaultEndpoints()</c>, so the
/// operational behaviour (logs, health endpoints, error format) is the same everywhere.
/// This mirrors the "ServiceDefaults" project that .NET Aspire generates.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>Tag for checks that only prove the process is alive.</summary>
    public const string LiveTag = "live";

    /// <summary>Tag for checks that prove the service can do useful work (database, broker, ...).</summary>
    public const string ReadyTag = "ready";

    /// <summary>Response header carrying the trace id, so a caller can look the request up in the dashboard.</summary>
    public const string TraceIdHeader = "X-Trace-Id";

    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();

        // One log line per event, with timestamp and the scope (which includes the TraceId),
        // so `docker compose logs` output from several services can be correlated.
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.IncludeScopes = true;
            options.TimestampFormat = "HH:mm:ss.fff ";
        });
        builder.Logging.Configure(options =>
            options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

        // Liveness: "is the process running?" It must not depend on the database or broker,
        // otherwise an orchestrator would restart a healthy process just because MySQL is slow.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("Process is running"), tags: [LiveTag]);

        // Errors are returned as RFC 7807 ProblemDetails JSON, the same shape from every service.
        builder.Services.AddProblemDetails();

        return builder;
    }

    /// <summary>
    /// Observability with OpenTelemetry, the vendor-neutral standard:
    /// <list type="bullet">
    ///   <item><b>Traces</b>: the path of one request through every service (spans with parent/child links).</item>
    ///   <item><b>Metrics</b>: numbers over time (request rate, latency, GC, ...).</item>
    ///   <item><b>Logs</b>: the ILogger output, tagged with the trace id of the request that wrote it.</item>
    /// </list>
    /// Everything is sent over OTLP to the address in OTEL_EXPORTER_OTLP_ENDPOINT (the Aspire
    /// Dashboard in docker compose). Swap the dashboard for Jaeger, Grafana, Seq or Azure Monitor
    /// without changing this code.
    /// </summary>
    private static void ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            // service.name is how the dashboard tells services apart ("Catalog.Api", "Orders.Api", ...).
            .ConfigureResource(resource => resource.AddService(
                builder.Configuration["OTEL_SERVICE_NAME"] ?? builder.Environment.ApplicationName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("System.Runtime"))
            .WithTracing(tracing => tracing
                // Incoming HTTP requests (health probes left out: they'd drown out real traffic).
                .AddAspNetCoreInstrumentation(o => o.Filter = context => !IsHealthCheck(context.Request.Path))
                // Outgoing HTTP calls: Orders -> Catalog, and the gateway -> every service.
                .AddHttpClientInstrumentation(o => o.FilterHttpRequestMessage = request => !IsHealthCheck(request.RequestUri?.AbsolutePath))
                // SQL statements. Only those that belong to a request or message; the outbox
                // loop's polling query every second would otherwise create a trace per second.
                .AddEntityFrameworkCoreInstrumentation(o => o.Filter = (_, _) => Activity.Current?.Parent is not null)
                // Our own RabbitMQ publish/process spans.
                .AddSource(MessagingTelemetry.ActivitySourceName));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }
    }

    private static bool IsHealthCheck(PathString path) => path.StartsWithSegments("/health");

    private static bool IsHealthCheck(string? path) => path is not null && IsHealthCheck(new PathString(path));

    /// <summary>Middleware every service runs first.</summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        // Return the trace id on every response. Set just before the headers are sent, so it
        // overwrites the copy the gateway receives from the downstream service (same id anyway).
        app.Use((context, next) =>
        {
            var traceId = Activity.Current?.TraceId.ToHexString();
            if (traceId is not null)
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers[TraceIdHeader] = traceId;
                    return Task.CompletedTask;
                });
            }

            return next(context);
        });

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    /// <summary>
    /// /health/live  -> liveness  (used to decide "restart me?")
    /// /health/ready -> readiness (used to decide "send me traffic?")
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(LiveTag),
            ResponseWriter = WriteHealthJson,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteHealthJson,
        });

        return app;
    }

    // The default writer only prints "Healthy". Printing each check makes the endpoint useful to read.
    private static Task WriteHealthJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                error = e.Value.Exception?.Message,
            }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
    }
}
