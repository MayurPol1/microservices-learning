using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Domain;

namespace Orders.Api.Data;

/// <summary>Orders' private database. It has no products table: product data belongs to Catalog.</summary>
public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("orders");
            order.HasKey(o => o.Id);
            order.Property(o => o.CustomerEmail).HasMaxLength(320).IsRequired();
            order.HasIndex(o => o.CustomerId); // "my orders" is a common query
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(o => o.Total).HasPrecision(18, 2);
            order.Property(o => o.RejectionReason).HasMaxLength(500);

            order.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
            order.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OrderLine>(line =>
        {
            line.ToTable("order_lines");
            line.HasKey(l => l.Id);
            line.Property(l => l.ProductName).HasMaxLength(200).IsRequired();
            line.Property(l => l.UnitPrice).HasPrecision(18, 2);
            line.Ignore(l => l.LineTotal);
        });

        modelBuilder.AddMessagingTables();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // MySQL DATETIME has no time zone. We always store UTC, but values read back come out as
        // DateTimeKind.Unspecified and are serialised without the trailing "Z", so a browser would
        // read them as local time. Mark every DateTime (and DateTime?) read from the database as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        toDatabase => toDatabase.Kind == DateTimeKind.Utc ? toDatabase : toDatabase.ToUniversalTime(),
        fromDatabase => DateTime.SpecifyKind(fromDatabase, DateTimeKind.Utc));
}
