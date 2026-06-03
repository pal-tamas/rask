namespace Rask.Core.Components;

// SVG group container. Carries only the inherited presentation attributes (notably Transform).
public sealed class G : SvgElement
{
    protected override string TagName => "g";
}
