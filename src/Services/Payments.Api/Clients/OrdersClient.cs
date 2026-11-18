using System.Net;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Payments.Api.Clients;

/// <summary>Payments' own view of an order: only the fields it needs ("tolerant reader", like Orders' view of Catalog).</summary>
public record OrderInfo(Guid Id, Guid CustomerId, string CustomerEmail, string Status, decimal Total, List<OrderLineInfo> Lines);

public record OrderLineInfo(string ProductName, decimal UnitPrice, int Quantity);

public class OrdersUnavailableException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Synchronous call to Orders, made <b>as the caller</b>: the customer's own "Authorization: Bearer ..." header is
/// passed on (token relay). So Orders applies its usual rules: a customer gets their own order, and someone
/// else's order is a 404 here too. Payments doesn't need its own copy of who owns what.
/// </summary>
public class OrdersClient(HttpClient http, ILogger<OrdersClient> logger)
{
    /// <summary>The order, or null if it doesn't exist or the caller may not see it.</summary>
    public async Task<OrderInfo?> GetOrderAsync(Guid orderId, string authorizationHeader, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/orders/{orderId}");
        request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OrderInfo>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
        {
            logger.LogWarning("Orders call failed after retries: {ErrorType}: {Error}", ex.GetType().Name, ex.Message);
            throw new OrdersUnavailableException("The Orders service is not available right now. Try again shortly.", ex);
        }
    }
}
