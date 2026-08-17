using System.Text;

namespace Rask.Core.Components;

// SVG <clipPath>. Note: the inherited ClipPath presentation property (the `clip-path` attribute)
// and this element type share a name but are distinct symbols — harmless.

/// <summary>
///     A shape that clips another element: everything outside it is not drawn. A hard-edged cut — for a
///     soft one, use a <c>mask</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/clipPath">MDN</see>
/// </summary>
public sealed class ClipPath : SvgElement
{
    protected override string TagName => "clipPath";

    /// <summary>
    ///     The coordinate system for the clipping shape: <c>userSpaceOnUse</c> or <c>objectBoundingBox</c>.
    /// </summary>
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
