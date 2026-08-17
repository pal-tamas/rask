using System.Text;

namespace Rask.Html.Components;

// Shared base for the table-column elements (Col, Colgroup), mirroring the DOM
// `HTMLTableColElement` interface. Both carry the `span` attribute; Col additionally self-closes
// (SelfClosing stays on Col, since Colgroup does not). Neither adds extra attributes.

/// <summary>
///     The attribute <c>col</c> and <c>colgroup</c> share. Not a tag of its own. <see
///     href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLTableColElement">MDN:
///     HTMLTableColElement</see>
/// </summary>
public abstract partial class HtmlTableColElement : Element
{
    /// <summary>How many consecutive columns the element applies to.</summary>
    public new int? Span { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Span is { } span)
        {
            AppendAttr(sb, "span", span);
        }
    }
}
