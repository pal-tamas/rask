using System.Text;

namespace Rask.Core.Components;

// Shared base for the document-modification elements (Ins, Del), mirroring the DOM
// `HTMLModElement` interface. Both carry the same cite/datetime attributes; neither adds extras,
// so they derive from this base with no body of their own. `cite` is URL-sanitized.
public abstract class HtmlModElement : Element
{
    public new string? Cite { get; set; }
    public string? DateTime { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Cite is not null)
        {
            AppendUrlAttr(sb, "cite", Cite);
        }

        if (DateTime is not null)
        {
            AppendAttr(sb, "datetime", DateTime);
        }
    }
}
