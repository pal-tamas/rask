namespace Rask.Site.Features;

public sealed class NestedOrderModel
{
    public string CustomerName { get; set; } = "";
    public NestedOrderAddress Address { get; set; } = new();
    public IList<NestedOrderLine> Lines { get; set; } = [];
}
