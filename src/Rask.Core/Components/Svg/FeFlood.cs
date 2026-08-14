using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     Fills the whole filter region with one colour — the paint source for a shadow or a tint.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/feFlood">MDN</see>
/// </summary>
public sealed class FeFlood : SvgElement
{
    protected override string TagName => "feFlood";

    /// <summary>The colour to fill with.</summary>
    public string? FloodColor { get; set; }

    /// <summary>The fill's opacity, from 0 to 1.</summary>
    public string? FloodOpacity { get; set; }

    /// <summary>A name for this primitive's output.</summary>
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (FloodColor is not null)
        {
            AppendAttr(sb, "flood-color", FloodColor);
        }

        if (FloodOpacity is not null)
        {
            AppendAttr(sb, "flood-opacity", FloodOpacity);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
