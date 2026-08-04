namespace Rask.Example.Shop.Features.Orders;

// The shared form model for the create + edit slices; maps onto Order.Create/Update.
public sealed class OrderRequest
{
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}
