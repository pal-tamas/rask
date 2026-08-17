using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     Hides parts of an element by luminance: white in the mask shows, black hides, and grey gives partial
///     transparency — so unlike a <c>clipPath</c>, the edges can be soft.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/mask">MDN</see>
/// </summary>
public sealed class Mask : SvgElement
{
    protected override string TagName => "mask";

    /// <summary>
    ///     The coordinate system for the mask's own region: <c>userSpaceOnUse</c> or
    ///     <c>objectBoundingBox</c>.
    /// </summary>
    public string? MaskUnits { get; set; }

    /// <summary>The coordinate system for the mask's contents.</summary>
    public string? MaskContentUnits { get; set; }

    /// <summary>The left edge of the masking region.</summary>
    public string? X { get; set; }

    /// <summary>The top edge of the masking region.</summary>
    public string? Y { get; set; }

    /// <summary>The masking region's width.</summary>
    public string? Width { get; set; }

    /// <summary>The masking region's height.</summary>
    public string? Height { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (MaskUnits is not null)
        {
            AppendAttr(sb, "maskUnits", MaskUnits);
        }

        if (MaskContentUnits is not null)
        {
            AppendAttr(sb, "maskContentUnits", MaskContentUnits);
        }

        if (X is not null)
        {
            AppendAttr(sb, "x", X);
        }

        if (Y is not null)
        {
            AppendAttr(sb, "y", Y);
        }

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width);
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height);
        }
    }
}
