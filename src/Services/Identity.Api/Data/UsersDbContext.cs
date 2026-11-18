using Identity.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Data;

/// <summary>Identity's private database (identity-db). Database per service, as for Catalog and Orders.</summary>
public class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Email).HasMaxLength(320).IsRequired();
            user.HasIndex(u => u.Email).IsUnique(); // one account per email
            user.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            user.Property(u => u.Role).HasMaxLength(20).IsRequired();
        });
    }
}
