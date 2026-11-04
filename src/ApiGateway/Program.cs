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

// Routes and backend addresses come from the "ReverseProxy" section of appsettings.json.
// In Docker the addresses are overridden with environment variables (see docker-compose.yml).
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseServiceDefaults();
app.UseRateLimiter();

app.MapGet("/", () => new
{
    name = "MicroShop API gateway",
    routes = new[] { "/api/products", "/api/orders" },
});

app.MapReverseProxy();
app.MapDefaultEndpoints();

app.Run();
