using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
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

    /// <summary>Middleware every service runs first.</summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
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
