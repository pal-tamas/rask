// The rask.sh hero's first file, compiled here. Rask.Site's HeroCode shows everything after the namespace line, and
// Rask.Site.Tests' HeroCodeSourceTests fails when the two differ — so edit this file, then copy it into HeroCode.
namespace Rask.Testing.Database.Tests.Hero;

// A value object carries its rule; a typed id is one line.
public readonly partial record struct ProductId(Guid Value);
public sealed partial record ProductName(string Value)
    : Text(Value, MaxLength: 200);
public sealed partial record UnitPrice(decimal Value)
    : Number(Value, Min: 0, Scale: 2);

// The aggregate is the table, and each method is one write.
public sealed partial class Product
    : Aggregate<ProductId, Timestamps, Versioned, Deletable>
{
    public ProductName Name { get; private set; } = null!;
    public UnitPrice Price { get; private set; } = null!;
    public bool Discontinued { get; private set; }

    // A factory: Product.Add(…) saves what it makes.
    private static Product Add(ProductName n, UnitPrice p) =>
        new() { Id = ProductId.New(), Name = n, Price = p };

    public void Reprice(UnitPrice price)
    {
        Refuse.When(Discontinued, () => Price,
            "A discontinued product keeps its price.");
        Price = price;
    }

    public void Discontinue() => Discontinued = true;
}
