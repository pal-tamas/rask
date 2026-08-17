using System.Text;

namespace Rask.Html.Components;

public sealed partial class Base : Element
{
    protected override string TagName => "base";
    protected override bool SelfClosing => true;

    public string? Href { get; set; }
    public string? Target { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }

        if (Target is not null)
        {
            AppendAttr(sb, "target", Target);
        }
    }
}
