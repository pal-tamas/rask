using System.Text;

namespace Rask.Html.Components;

// SVG <a> hyperlink. Named SvgA to avoid colliding with the HTML A component.
public sealed partial class SvgA : SvgElement
{
    protected override string TagName => "a";

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
