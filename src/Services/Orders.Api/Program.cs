using BuildingBlocks.Messaging;
using BuildingBlocks.Security;
using BuildingBlocks.Persistence;
using BuildingBlocks.ServiceDefaults;
using Contracts;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;
using Orders.Api.Clients;
using Orders.Api.Data;
using Orders.Api.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Validates the JWT that Identity issued (same key and rules as every service).
builder.AddJwtAuthentication();

// Orders' own database, separate from Catalog's.
builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("OrdersDb")!));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrdersDbContext>("orders-db", tags: [ServiceDefaultsExtensions.ReadyTag]);

// Typed HttpClient for Catalog. The address comes from configuration: http://localhost:5101/ locally,
// http://catalog-api:8080/ inside Docker (compose's DNS resolves the service name).
// That lookup is the simplest form of "service discovery".
builder.Services.AddHttpClient<CatalogClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]
            ?? throw new InvalidOperationException("Missing configuration value Services:Catalog")))
    // The standard resilience pipeline, outermost to innermost:
    //   total timeout -> retry -> circuit breaker -> per-attempt timeout
    .AddStandardResilienceHandler(options =>
    {
        // Retry transient failures (5xx, 408, 429, network errors) with exponential backoff + jitter.
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(300);

        // Never wait longer than this for one attempt, or for the whole call including retries.
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);

        // If at least half of the last 5+ calls in 30s failed, stop calling Catalog for 15s
        // and fail fast instead. This gives Catalog room to recover and keeps Orders responsive.
        // (Small numbers so you can trip it by hand; the library defaults are much larger.)
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    });

// Asynchronous messaging: Orders publishes order.placed (via the outbox) and listens for
// Catalog's answer and Payments' news on its own "orders" queue.
builder.AddMessaging(queueName: "orders")
    .Subscribe<StockReserved, StockEventsHandler>(RoutingKeys.StockReserved)
    .Subscribe<StockReservationFailed, StockEventsHandler>(RoutingKeys.StockReservationFailed)
    .Subscribe<PaymentSucceeded, PaymentEventsHandler>(RoutingKeys.PaymentSucceeded)
    .AddOutbox<OrdersDbContext>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseServiceDefaults();
app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication(); // Bearer token -> HttpContext.User
app.UseAuthorization();  // [Authorize] and roles

app.MapControllers();
app.MapDefaultEndpoints();

await app.EnsureDatabaseAsync<OrdersDbContext>();

app.Run();
