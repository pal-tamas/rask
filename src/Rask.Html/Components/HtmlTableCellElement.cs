using System.Text;

namespace Rask.Html.Components;

// Shared base for the table-cell elements (Td, Th), mirroring the DOM `HTMLTableCellElement`
// interface. Holds the colspan/rowspan/headers attributes common to both; Th adds scope/abbr,
// which emit after this shared block (base.WriteAttributes runs first).
public abstract partial class HtmlTableCellElement : Element
{
    public int? Colspan { get; set; }
    public int? Rowspan { get; set; }
    public string? Headers { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Colspan is { } colspan)
        {
            AppendAttr(sb, "colspan", colspan);
        }

        if (Rowspan is { } rowspan)
        {
            AppendAttr(sb, "rowspan", rowspan);
        }

        if (Headers is not null)
        {
            AppendAttr(sb, "headers", Headers);
        }
    }
}
