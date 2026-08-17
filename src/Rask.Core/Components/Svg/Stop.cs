using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     One colour stop in a gradient. Order the stops by ascending <c>Offset</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/stop">MDN</see>
/// </summary>
public sealed class Stop : SvgElement
{
    protected override string TagName => "stop";

    /// <summary>Where the stop sits along the gradient, as a number from 0 to 1 or a percentage.</summary>
    public string? Offset { get; set; }

    /// <summary>The colour at this stop.</summary>
    public string? StopColor { get; set; }

    /// <summary>The opacity at this stop, from 0 to 1.</summary>
    public string? StopOpacity { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Offset is not null)
        {
            AppendAttr(sb, "offset", Offset);
        }

        if (StopColor is not null)
        {
            AppendAttr(sb, "stop-color", StopColor);
        }

        if (StopOpacity is not null)
        {
            AppendAttr(sb, "stop-opacity", StopOpacity);
        }
    }
}
