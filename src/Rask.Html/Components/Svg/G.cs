namespace Rask.Html.Components;

// SVG group container. Carries only the inherited presentation attributes (notably Transform).
public sealed partial class G : SvgElement
{
    protected override string TagName => "g";
}
