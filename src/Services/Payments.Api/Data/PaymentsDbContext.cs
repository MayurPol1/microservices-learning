using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Payments.Api.Domain;

namespace Payments.Api.Data;

/// <summary>
/// Payments' private database: payment attempts, the orders it knows were cancelled, and the usual
/// outbox and inbox tables. It stores Stripe's ids, never card details: those stay on Stripe's page.
/// </summary>
public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CancelledOrder> CancelledOrders => Set<CancelledOrder>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(payment =>
        {
            payment.ToTable("payments");
            payment.HasKey(p => p.Id);
            payment.Property(p => p.CustomerEmail).HasMaxLength(256).IsRequired();
            payment.Property(p => p.Amount).HasPrecision(18, 2);
            payment.Property(p => p.Currency).HasMaxLength(Payment.CurrencyMaxLength).IsRequired();
            payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            payment.Property(p => p.StripeSessionId).HasMaxLength(Payment.StripeIdMaxLength);
            payment.Property(p => p.CheckoutUrl).HasMaxLength(Payment.UrlMaxLength);
            payment.Property(p => p.StripePaymentIntentId).HasMaxLength(Payment.StripeIdMaxLength);
            payment.Property(p => p.StripeRefundId).HasMaxLength(Payment.StripeIdMaxLength);
            payment.HasIndex(p => p.OrderId);
            payment.HasIndex(p => p.StripeSessionId).IsUnique();
        });

        modelBuilder.Entity<CancelledOrder>(cancelled =>
        {
            cancelled.ToTable("cancelled_orders");
            cancelled.HasKey(c => c.OrderId);
            cancelled.Property(c => c.OrderId).ValueGeneratedNever();
        });

        modelBuilder.AddMessagingTables();
    }
}
