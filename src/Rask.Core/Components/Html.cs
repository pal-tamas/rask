using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     The document's root element. Everything else lives inside it, and setting <c>Lang</c> here is the
///     single highest-value accessibility attribute on the page.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/html">MDN</see>
/// </summary>
public sealed class Html : Element
{
    protected override string TagName => "html";

    /// <summary>The XML namespace. Needed only when the document is served as XHTML.</summary>
    public string? Xmlns { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Xmlns is not null)
        {
            AppendAttr(sb, "xmlns", Xmlns);
        }
    }
}
