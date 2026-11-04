using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Orders.Api.Clients;

/// <summary>
/// Orders' own view of a Catalog product: only the fields Orders needs.
/// Orders does NOT reference Catalog's DTO classes. If Catalog adds a field, nothing here
/// breaks ("tolerant reader"); the JSON shape is the contract, not a shared C# type.
/// </summary>
public record CatalogProduct(Guid Id, string Name, decimal Price, int AvailableStock);

public class CatalogUnavailableException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Typed HttpClient for synchronous calls to the Catalog service.
/// The HttpClient it receives already has retry, timeout and circuit breaker wired in
/// (see Program.cs), so this class only deals with the happy path and the final failure.
/// </summary>
public class CatalogClient(HttpClient http, ILogger<CatalogClient> logger)
{
    public async Task<IReadOnlyList<CatalogProduct>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        // One call for all lines (GET api/products?ids=a&ids=b), not one call per line.
        // Chatty service-to-service calls are a classic microservices performance trap.
        var query = string.Join("&", ids.Select(id => $"ids={id}"));

        try
        {
            return await http.GetFromJsonAsync<List<CatalogProduct>>($"api/products?{query}", ct) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
        {
            // Reached only after the resilience pipeline has given up (retries exhausted,
            // total timeout hit, or circuit open).
            logger.LogWarning("Catalog call failed after retries: {ErrorType}: {Error}", ex.GetType().Name, ex.Message);
            throw new CatalogUnavailableException("The Catalog service is not available right now. Try again shortly.", ex);
        }
    }
}
