namespace Rask.Example.Shop.Features.Products;

// The shared form model for the create + edit slices; maps onto Product.Create/Update.
public sealed class ProductRequest
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool InStock { get; set; }

    public int Version { get; set; }
}
