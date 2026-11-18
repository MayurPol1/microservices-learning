using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using BuildingBlocks.ServiceDefaults;
using Contracts;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;
using Payments.Api.Application;
using Payments.Api.Clients;
using Payments.Api.Data;
using Payments.Api.Messaging;
using Payments.Api.PaymentGateway;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Validates the JWT that Identity issued (same key and rules as every service).
builder.AddJwtAuthentication();

// Payments' own database: payment attempts and Stripe ids. Never card numbers.
builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("PaymentsDb")!));

// Readiness depends on the database only. If Stripe is down or not configured, the service is still
// "ready": checkout answers with a clear error and everything else keeps working (graceful degradation).
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PaymentsDbContext>("payments-db", tags: [ServiceDefaultsExtensions.ReadyTag]);

builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.SectionName));
builder.Services.Configure<PaymentsOptions>(builder.Configuration.GetSection(PaymentsOptions.SectionName));
builder.Services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PaymentWorkflow>();

// Synchronous call to Orders (to read an order before paying for it), with the same resilience
// pipeline as Orders -> Catalog: timeouts, retries with backoff, circuit breaker.
builder.Services.AddHttpClient<OrdersClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Orders"]
            ?? throw new InvalidOperationException("Missing configuration value Services:Orders")))
    .AddStandardResilienceHandler();

// Its own "payments" queue. It listens for cancelled orders (close the Stripe page or refund) and
// publishes payment.succeeded / payment.refunded through its outbox.
builder.AddMessaging(queueName: "payments")
    .Subscribe<OrderCancelled, OrderCancelledHandler>(RoutingKeys.OrderCancelled)
    .AddOutbox<PaymentsDbContext>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseServiceDefaults();
app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication(); // Bearer token -> HttpContext.User
app.UseAuthorization();  // [Authorize]; the Stripe webhook is [AllowAnonymous] and checks Stripe's signature instead

app.MapControllers();
app.MapDefaultEndpoints();

await app.EnsureDatabaseAsync<PaymentsDbContext>();

app.Run();
