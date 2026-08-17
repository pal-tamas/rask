using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     Shifts its input by a fixed amount — the displacement half of a hand-built drop shadow.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/feOffset">MDN</see>
/// </summary>
public sealed class FeOffset : SvgElement
{
    protected override string TagName => "feOffset";

    /// <summary>The input for this primitive.</summary>
    public string? In { get; set; }

    /// <summary>The horizontal shift.</summary>
    public string? Dx { get; set; }

    /// <summary>The vertical shift.</summary>
    public string? Dy { get; set; }

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

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
