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
            new Product(KeyboardId, "Mechanical Keyboard", "Tactile switches, US layout", 79.99m, 10, "Peripherals"),
            new Product(MouseId, "Wireless Mouse", "2.4 GHz, 3 buttons", 24.50m, 25, "Peripherals"),
            new Product(MonitorId, "27-inch Monitor", "1440p, 144 Hz", 229.00m, 3, "Displays"),
            new Product(CableId, "USB-C Cable", "1 m, braided", 9.99m, 100, "Accessories"),
            // A few more so the web app's category filter and paging have something to show.
            new Product(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Laptop Stand", "Aluminium, adjustable height", 39.00m, 15, "Accessories"),
            new Product(Guid.Parse("66666666-6666-6666-6666-666666666666"), "Noise-cancelling Headphones", "Over-ear, 30 h battery", 149.00m, 8, "Audio"),
            new Product(Guid.Parse("77777777-7777-7777-7777-777777777777"), "Webcam 1080p", "Autofocus, built-in microphone", 59.90m, 12, "Peripherals"),
            new Product(Guid.Parse("88888888-8888-8888-8888-888888888888"), "Desk Mat", "90 x 40 cm, stitched edges", 19.50m, 40, "Accessories"),
            new Product(Guid.Parse("99999999-9999-9999-9999-999999999999"), "34-inch Ultrawide Monitor", "3440 x 1440, 100 Hz", 399.00m, 2, "Displays"),
            new Product(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Bluetooth Speaker", "Waterproof, 12 h battery", 45.00m, 20, "Audio"));

        await db.SaveChangesAsync();
    }
}
