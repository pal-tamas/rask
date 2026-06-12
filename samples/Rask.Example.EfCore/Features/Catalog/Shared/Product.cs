namespace Rask.Example.EfCore.Features.Catalog.Shared;

// The Catalog aggregate root. State is encapsulated behind private setters and only changes
// through Create / Update, which compose the value objects — so an invalid Product can't exist.
// Slices construct and mutate it through these methods (the ubiquitous language), never by
// reaching in and setting properties.
public sealed class Product
{
    // EF Core materialisation ctor — never used by application code.
    private Product()
    {
    }

    private Product(ProductName name, Money price, StockLevel stock)
    {
        Name = name;
        Price = price;
        Stock = stock;
    }

    public int Id { get; private set; }

    public ProductName Name { get; private set; }

    public Money Price { get; private set; }

    public StockLevel Stock { get; private set; }

    public static Product Create(string name, decimal price, int stock) =>
        new(ProductName.From(name), Money.FromDecimal(price), StockLevel.From(stock));

    public void Update(string name, decimal price, int stock)
    {
        Name = ProductName.From(name);
        Price = Money.FromDecimal(price);
        Stock = StockLevel.From(stock);
    }
}
