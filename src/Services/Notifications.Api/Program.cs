using BuildingBlocks.Messaging;
using BuildingBlocks.Security;
using BuildingBlocks.ServiceDefaults;
using Contracts;
using Notifications.Api;
using Notifications.Api.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Validates the JWT that Identity issued (same key and rules as every service).
builder.AddJwtAuthentication();

builder.Services.AddSingleton<NotificationStore>();

// Its own "notifications" queue, bound to three routing keys. order.placed now reaches two
// queues (catalog and notifications): each subscriber gets its own copy (fan-out).
// No outbox here: this service only consumes, it never publishes.
builder.AddMessaging(queueName: "notifications")
    .Subscribe<OrderPlaced, OrderNotificationHandlers>(RoutingKeys.OrderPlaced)
    .Subscribe<OrderConfirmed, OrderNotificationHandlers>(RoutingKeys.OrderConfirmed)
    .Subscribe<OrderRejected, OrderNotificationHandlers>(RoutingKeys.OrderRejected)
    .Subscribe<OrderCancelled, OrderNotificationHandlers>(RoutingKeys.OrderCancelled);

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

app.Run();
