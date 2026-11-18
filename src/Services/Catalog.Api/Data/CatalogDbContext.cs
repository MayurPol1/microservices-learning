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
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

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
            product.Property(p => p.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
            product.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
            product.Property(p => p.Price).HasPrecision(18, 2);
            product.Property(p => p.Category).HasMaxLength(Product.CategoryMaxLength).IsRequired();
            product.Property(p => p.Version).IsConcurrencyToken();
            product.HasIndex(p => p.Category);
        });

        modelBuilder.Entity<StockReservation>(reservation =>
        {
            reservation.ToTable("stock_reservations");
            reservation.HasKey(r => r.OrderId);
            reservation.Property(r => r.OrderId).ValueGeneratedNever();
            reservation.Ignore(r => r.IsReleased);
            reservation.HasMany(r => r.Lines).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
            reservation.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<StockReservationLine>(line =>
        {
            line.ToTable("stock_reservation_lines");
            line.HasKey(l => l.Id);
        });

        modelBuilder.AddMessagingTables();
    }
}
