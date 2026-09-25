namespace Rask.Site.Features;

public sealed class InvoiceModel
{
    public IList<SkuRow> Skus { get; set; } = [];
}
