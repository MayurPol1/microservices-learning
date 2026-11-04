using Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Data;

/// <summary>Demo products with fixed IDs, so the README and scripts can refer to them.</summary>
public static class CatalogSeed
{
    public static readonly Guid KeyboardId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid MouseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid MonitorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid CableId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static async Task SeedAsync(CatalogDbContext db)
    {
        if (await db.Products.AnyAsync())
        {
            return;
        }

        db.Products.AddRange(
            new Product(KeyboardId, "Mechanical Keyboard", "Tactile switches, US layout", 79.99m, 10),
            new Product(MouseId, "Wireless Mouse", "2.4 GHz, 3 buttons", 24.50m, 25),
            new Product(MonitorId, "27-inch Monitor", "1440p, 144 Hz", 229.00m, 3),
            new Product(CableId, "USB-C Cable", "1 m, braided", 9.99m, 100));

        await db.SaveChangesAsync();
    }
}
