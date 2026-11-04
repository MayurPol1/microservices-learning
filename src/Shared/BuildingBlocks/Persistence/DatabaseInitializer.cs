using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Creates this service's tables (and optional seed data) at startup.
    /// <para>
    /// Retries because in a distributed system "the database is up" is never guaranteed at
    /// the moment your process starts, even with docker compose's <c>depends_on</c>.
    /// </para>
    /// <para>
    /// EnsureCreated keeps the learning project short. A production service would ship EF Core
    /// migrations and apply them in its own deployment step, but the idea is the same: each
    /// service owns and evolves its own schema; no other team runs DDL against it.
    /// </para>
    /// </summary>
    public static async Task EnsureDatabaseAsync<TContext>(
        this WebApplication app,
        Func<TContext, Task>? seed = null,
        int maxAttempts = 20)
        where TContext : DbContext
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();

                await db.Database.EnsureCreatedAsync();
                if (seed is not null)
                {
                    await seed(db);
                }

                logger.LogInformation("Database for {DbContext} is ready", typeof(TContext).Name);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("Database not reachable yet (attempt {Attempt}/{Max}): {Error}. Retrying in 3s",
                    attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
