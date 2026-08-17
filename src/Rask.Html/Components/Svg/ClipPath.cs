using System.Text;

namespace Rask.Html.Components;

// SVG <clipPath>. Note: the inherited ClipPath presentation property (the `clip-path` attribute)
// and this element type share a name but are distinct symbols — harmless.
public sealed partial class ClipPath : SvgElement
{
    protected override string TagName => "clipPath";

    public string? ClipPathUnits { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (ClipPathUnits is not null)
        {
            AppendAttr(sb, "clipPathUnits", ClipPathUnits);
        }
    }
}
