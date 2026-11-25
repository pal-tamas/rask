// The rask.sh hero's third file, compiled and run here (see Product.cs).
// The front page has room for the rule, not for a TestContext.Current.CancellationToken on every call: the test is
// shown as short as it can be written, and it finishes in milliseconds on SQLite in memory, so nothing is lost here.
#pragma warning disable xUnit1051
namespace Rask.Testing.Database.Tests.Hero;

// SQLite in memory: a database per test, run side by side.
public sealed class ProductTests
{
    [Fact]
    public async Task A_discontinued_product_keeps_its_price()
    {
        await using var db = await TestDatabase.Start();
        var id = await Product.Add(Test.Make<ProductName>(),
            UnitPrice.Create(9.90m));
        var product = (await Product.Find(id))!;
        product.Discontinue();
        await product.Save();

        var cheaper = UnitPrice.Create(5m);
        var refused = await Test.Refuses(() =>
            Product.Reprice(id, product.Version, cheaper));

        refused.Only("Price");
    }
}
