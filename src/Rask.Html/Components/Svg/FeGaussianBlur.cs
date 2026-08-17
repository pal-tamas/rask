using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     Blurs its input with a Gaussian kernel.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/feGaussianBlur">MDN</see>
/// </summary>
public sealed partial class FeGaussianBlur : SvgElement
{
    protected override string TagName => "feGaussianBlur";

    /// <summary>
    ///     The input for this primitive: <c>SourceGraphic</c>, <c>SourceAlpha</c>, or the <c>Result</c>
    ///     name of an earlier one.
    /// </summary>
    public string? In { get; set; }

    /// <summary>
    ///     The blur's standard deviation. One value blurs both axes equally; two blur x and y separately.
    /// </summary>
    public string? StdDeviation { get; set; }

    /// <summary>
    ///     How to extend the input past its edges: <c>duplicate</c>, <c>wrap</c>, or <c>none</c>.
    /// </summary>
    public string? EdgeMode { get; set; }

    /// <summary>A name for this primitive's output, so a later one can take it as <c>In</c>.</summary>
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }

        if (StdDeviation is not null)
        {
            AppendAttr(sb, "stdDeviation", StdDeviation);
        }

        if (EdgeMode is not null)
        {
            AppendAttr(sb, "edgeMode", EdgeMode);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
