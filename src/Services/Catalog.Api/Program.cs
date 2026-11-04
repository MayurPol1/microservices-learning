using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.ServiceDefaults;
using Catalog.Api.Data;
using Catalog.Api.Messaging;
using Contracts;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Logging, health checks and error format shared by every service.
builder.AddServiceDefaults();

// Catalog's own database. The connection string comes from configuration:
// appsettings.json for local runs, the ConnectionStrings__CatalogDb environment variable in Docker.
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("CatalogDb")!));

// Readiness depends on the database: if MySQL is down, this service can't serve requests.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>("catalog-db", tags: [ServiceDefaultsExtensions.ReadyTag]);

// Asynchronous messaging: Catalog's queue is called "catalog". It listens for new orders
// and publishes its own events through the outbox table.
builder.AddMessaging(queueName: "catalog")
    .Subscribe<OrderPlaced, OrderPlacedHandler>(RoutingKeys.OrderPlaced)
    .AddOutbox<CatalogDbContext>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseServiceDefaults();

// Chaos switch for the resilience demo: make product reads fail or slow down on purpose,
// then watch Orders retry, time out and open its circuit breaker. Off by default.
var chaos = app.Configuration.GetSection("Chaos");
var failureRate = chaos.GetValue<double>("FailureRate");
var extraLatency = TimeSpan.FromMilliseconds(chaos.GetValue<int>("ExtraLatencyMs"));
if (failureRate > 0 || extraLatency > TimeSpan.Zero)
{
    app.Logger.LogWarning("Chaos mode ON: {FailureRate:P0} of product reads fail, +{Latency} ms latency",
        failureRate, extraLatency.TotalMilliseconds);

    app.Use(async (context, next) =>
    {
        var isProductRead = HttpMethods.IsGet(context.Request.Method)
            && context.Request.Path.StartsWithSegments("/api/products");

        if (isProductRead)
        {
            await Task.Delay(extraLatency, context.RequestAborted);
            if (Random.Shared.NextDouble() < failureRate)
            {
                app.Logger.LogWarning("Chaos: returning 503 for {Path}", context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
        }

        await next();
    });
}

// Swagger is on in every environment because this is a learning project.
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapDefaultEndpoints();

await app.EnsureDatabaseAsync<CatalogDbContext>(CatalogSeed.SeedAsync);

app.Run();
