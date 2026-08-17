using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A drop shadow in one primitive — the blur, offset, flood and composite chain done for you.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/feDropShadow">MDN</see>
/// </summary>
public sealed class FeDropShadow : SvgElement
{
    protected override string TagName => "feDropShadow";

    /// <summary>The input for this primitive.</summary>
    public string? In { get; set; }

    /// <summary>The shadow's horizontal offset.</summary>
    public string? Dx { get; set; }

    /// <summary>The shadow's vertical offset.</summary>
    public string? Dy { get; set; }

    /// <summary>The shadow's blur radius.</summary>
    public string? StdDeviation { get; set; }

    /// <summary>The shadow's colour.</summary>
    public string? FloodColor { get; set; }

    /// <summary>The shadow's opacity, from 0 to 1.</summary>
    public string? FloodOpacity { get; set; }

    /// <summary>A name for this primitive's output.</summary>
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }

        if (Dx is not null)
        {
            AppendAttr(sb, "dx", Dx);
        }

        if (Dy is not null)
        {
            AppendAttr(sb, "dy", Dy);
        }

        if (StdDeviation is not null)
        {
            AppendAttr(sb, "stdDeviation", StdDeviation);
        }

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
