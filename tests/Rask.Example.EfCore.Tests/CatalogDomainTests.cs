using Rask.Example.EfCore.Features.Catalog.Shared;

namespace Rask.Example.EfCore.Tests;

// The DDD core: the value objects own the validation rules (reused by the inline form validators)
// and the aggregate enforces them on construction. These are pure unit tests — no browser, no DB.
public sealed class CatalogDomainTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Money_Validate_RejectsNonPositive(decimal amount)
    {
        Assert.Contains("Price must be greater than zero.", Money.Validate(amount));
    }

    [Fact]
    public void Money_Validate_RejectsAboveMax()
    {
        Assert.NotEmpty(Money.Validate(Money.MaxAmount + 1m));
    }

    [Fact]
    public void Money_Validate_AcceptsValid() => Assert.Empty(Money.Validate(12.34m));

    [Fact]
    public void Money_StoresMinorUnits_AndRoundTrips()
    {
        var money = Money.FromDecimal(12.34m);
        Assert.Equal(1234, money.Cents);
        Assert.Equal(12.34m, money.Amount);
        Assert.Equal(money, Money.FromCents(money.Cents));
    }

    [Fact]
    public void Money_FromDecimal_RoundsToNearestCent()
    {
        Assert.Equal(1234, Money.FromDecimal(12.344m).Cents); // rounds down
        Assert.Equal(1235, Money.FromDecimal(12.346m).Cents); // rounds up
        Assert.Equal(1234, Money.FromDecimal(12.345m).Cents); // banker's rounding: .5 -> nearest even
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProductName_Validate_RequiresText(string value)
    {
        Assert.Contains("Name is required.", ProductName.Validate(value));
    }

    [Fact]
    public void ProductName_Validate_RejectsTooLong()
    {
        Assert.NotEmpty(ProductName.Validate(new string('x', ProductName.MaxLength + 1)));
    }

    [Fact]
    public void ProductName_From_Trims() => Assert.Equal("Widget", ProductName.From("  Widget  ").Value);

    [Fact]
    public void StockLevel_Validate_RejectsNegative() =>
        Assert.Contains("Stock cannot be negative.", StockLevel.Validate(-1));

    [Fact]
    public void StockLevel_Validate_AcceptsZeroAndPositive()
    {
        Assert.Empty(StockLevel.Validate(0));
        Assert.Empty(StockLevel.Validate(40));
    }

    [Fact]
    public void Product_Create_BuildsValidAggregate()
    {
        var product = Product.Create("Keyboard", 89.00m, 12);
        Assert.Equal("Keyboard", product.Name.Value);
        Assert.Equal(89.00m, product.Price.Amount);
        Assert.Equal(12, product.Stock.Value);
    }

    [Fact]
    public void Product_Create_RejectsInvalidInvariants()
    {
        Assert.Throws<ArgumentException>(() => Product.Create("", 10m, 1));
        Assert.Throws<ArgumentException>(() => Product.Create("Ok", 0m, 1));
        Assert.Throws<ArgumentException>(() => Product.Create("Ok", 10m, -1));
    }

    [Fact]
    public void Product_Update_AppliesNewValues()
    {
        var product = Product.Create("Old", 10m, 1);
        product.Update("New", 25.50m, 7);
        Assert.Equal("New", product.Name.Value);
        Assert.Equal(25.50m, product.Price.Amount);
        Assert.Equal(7, product.Stock.Value);
    }
}
