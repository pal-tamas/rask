using System.Text;

namespace Rask.Html.Components;

// Shared base for the quote elements (Q, Blockquote), mirroring the DOM `HTMLQuoteElement`
// interface. Both carry the URL-sanitized `cite` attribute and add nothing else.
public abstract partial class HtmlQuoteElement : Element
{
    public string? Cite { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Cite is not null)
        {
            AppendUrlAttr(sb, "cite", Cite);
        }
    }
}
