namespace Rask.Site.Features;

public sealed class CartModel
{
    public IList<LineItem> Items { get; set; } = [];
}
