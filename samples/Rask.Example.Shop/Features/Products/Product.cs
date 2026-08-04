namespace Rask.Example.Shop.Features.Products;

public sealed class Product : Entity<Guid>, ISoftDeletable, IVersioned
{
    private Product() { } // EF Core materialization

    private Product(ProductName name, decimal price, bool inStock)
    {
        Id = Guid.NewGuid();
        this.Name = name;
        this.Price = price;
        this.InStock = inStock;
    }

    public DateTime? DeletedAt { get; private set; }

    public void Restore() => DeletedAt = null;

    public int Version { get; private set; }

    public ProductName Name { get; private set; }

    public decimal Price { get; private set; }

    public bool InStock { get; private set; }

    public static Product Create(string name, decimal price, bool inStock) => new(ProductName.Create(name), price, inStock);

    public void Update(string name, decimal price, bool inStock)
    {
        this.Name = ProductName.Create(name);
        this.Price = price;
        this.InStock = inStock;
    }
}
