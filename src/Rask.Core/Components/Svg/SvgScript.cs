using System.Text;

namespace Rask.Core.Components;

// SVG <script>. Named SvgScript to avoid colliding with the HTML Script component.
public sealed class SvgScript : SvgElement
{
    protected override string TagName => "script";

    public string? Href { get; set; }
    public string? Type { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }
    }
}
