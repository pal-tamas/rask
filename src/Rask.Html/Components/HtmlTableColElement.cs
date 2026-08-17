using System.Text;

namespace Rask.Html.Components;

// Shared base for the table-column elements (Col, Colgroup), mirroring the DOM
// `HTMLTableColElement` interface. Both carry the `span` attribute; Col additionally self-closes
// (SelfClosing stays on Col, since Colgroup does not). Neither adds extra attributes.
public abstract partial class HtmlTableColElement : Element
{
    public int? Span { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Span is { } span)
        {
            AppendAttr(sb, "span", span);
        }
    }
}
