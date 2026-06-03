using System.Text;

namespace Rask.Core.Components;

// SVG <a> hyperlink. Named SvgA to avoid colliding with the HTML A component.
public sealed class SvgA : SvgElement
{
    protected override string TagName => "a";

    public string? Href { get; set; }
    public string? Target { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendAttr(sb, "href", Href);
        }

        if (Target is not null)
        {
            AppendAttr(sb, "target", Target);
        }
    }
}
