using System.Threading.RateLimiting;
using BuildingBlocks.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Cross-cutting concern handled once, at the edge, instead of in every service:
// each client IP may make PermitLimit requests per window; beyond that it gets 429.
var permitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100);
var window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:WindowSeconds", 10));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("per-client", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window }));
});

// CORS, also at the edge: browsers refuse to let a page from one origin (the Angular app on
// http://localhost:4200) read responses from another (this gateway on :5000) unless the server
// says that origin is allowed. Services behind the gateway never see browser traffic, so they
// need no CORS setup of their own.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("web-app", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    // Let the browser's JavaScript read these response headers (hidden by default).
    .WithExposedHeaders(ServiceDefaultsExtensions.TraceIdHeader, "Location")));

// Routes and backend addresses come from the "ReverseProxy" section of appsettings.json.
// In Docker the addresses are overridden with environment variables (see docker-compose.yml).
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseServiceDefaults();
// Before the rate limiter and the proxy, so browser "preflight" (OPTIONS) requests are answered here.
app.UseCors("web-app");
app.UseRateLimiter();

app.MapGet("/", () => new
{
    name = "MicroShop API gateway",
    routes = new[] { "/api/auth", "/api/products", "/api/orders", "/api/notifications" },
});

app.MapReverseProxy();
app.MapDefaultEndpoints();

app.Run();
