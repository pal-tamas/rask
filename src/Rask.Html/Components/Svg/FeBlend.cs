using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     Blends two inputs using a Porter-Duff or separable blend mode.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/feBlend">MDN</see>
/// </summary>
public sealed partial class FeBlend : SvgElement
{
    protected override string TagName => "feBlend";

    /// <summary>The first input.</summary>
    public string? In { get; set; }

    /// <summary>The second input, blended underneath the first.</summary>
    public string? In2 { get; set; }

    /// <summary>
    ///     The blend mode: <c>normal</c>, <c>multiply</c>, <c>screen</c>, <c>darken</c>, <c>lighten</c>,
    ///     and the rest of the CSS set.
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>A name for this primitive's output.</summary>
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }

        if (In2 is not null)
        {
            AppendAttr(sb, "in2", In2);
        }

        if (Mode is not null)
        {
            AppendAttr(sb, "mode", Mode);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
