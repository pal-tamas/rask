namespace Rask.Site.Features;

public sealed class StorefrontModel
{
    public string CustomerName { get; set; } = "";
    public StorefrontAddress Address { get; set; } = new();
    public IList<StorefrontLineItem> Items { get; set; } = [];
    public string DiscountCode { get; set; } = "";
}
