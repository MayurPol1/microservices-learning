using BuildingBlocks.Messaging.Outbox;
using Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Data;

/// <summary>
/// Catalog's private database. Database-per-service means: only this service has the
/// connection string, and other services get product data through Catalog's API or events,
/// never with a SQL JOIN across services.
/// </summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Product> Products => Set<Product>();

    // Outbox (events waiting to be published) and inbox (messages already handled),
    // in the same database so they share transactions with Products.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products");
            product.HasKey(p => p.Id);
            product.Property(p => p.Name).HasMaxLength(200).IsRequired();
            product.Property(p => p.Description).HasMaxLength(1000);
            product.Property(p => p.Price).HasPrecision(18, 2);
            product.Property(p => p.Version).IsConcurrencyToken();
        });

        modelBuilder.AddMessagingTables();
    }
}
