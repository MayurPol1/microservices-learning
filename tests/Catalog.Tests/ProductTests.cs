using Catalog.Api.Domain;

namespace Catalog.Tests;

/// <summary>Plain unit tests of the Product domain object: no database, no web host, no broker.</summary>
public class ProductTests
{
    private static Product NewProduct(int stock) => new(Guid.NewGuid(), "Keyboard", null, 50m, stock, "Peripherals");

    [Fact]
    public void Reserve_takes_stock_and_bumps_the_concurrency_version()
    {
        var product = NewProduct(stock: 10);

        product.Reserve(3);

        Assert.Equal(7, product.AvailableStock);
        Assert.Equal(1, product.Version);
    }

    [Fact]
    public void Reserve_more_than_available_throws_and_changes_nothing()
    {
        var product = NewProduct(stock: 2);

        Assert.Throws<InvalidOperationException>(() => product.Reserve(3));
        Assert.Equal(2, product.AvailableStock);
        Assert.Equal(0, product.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CanReserve_is_false_for_non_positive_quantities(int quantity) =>
        Assert.False(NewProduct(stock: 10).CanReserve(quantity));

    [Fact]
    public void Restock_adds_to_available_stock()
    {
        var product = NewProduct(stock: 1);

        product.Restock(4);

        Assert.Equal(5, product.AvailableStock);
    }

    [Fact]
    public void UpdateDetails_changes_details_but_never_stock()
    {
        var product = NewProduct(stock: 7);

        product.UpdateDetails("  Keyboard Pro  ", "  USB-C  ", 89.99m, " Accessories ");

        Assert.Equal("Keyboard Pro", product.Name);
        Assert.Equal("USB-C", product.Description);
        Assert.Equal(89.99m, product.Price);
        Assert.Equal("Accessories", product.Category);
        Assert.Equal(7, product.AvailableStock);
        Assert.Equal(1, product.Version);
    }

    [Theory]
    [InlineData("", "Peripherals")]
    [InlineData("Keyboard", " ")]
    public void A_product_needs_a_name_and_a_category(string name, string category) =>
        Assert.ThrowsAny<ArgumentException>(() => NewProduct(stock: 1).UpdateDetails(name, null, 1m, category));
}
