using System.Globalization;
using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A scalar measurement within a known range — disk usage, a relevance score. For task progress use
///     <c>progress</c> instead; a meter is a gauge, not a completion bar.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/meter">MDN</see>
/// </summary>
public sealed partial class Meter : Element
{
    protected override string TagName => "meter";

    /// <summary>
    ///     The current measurement. Required, and clamped into the <c>Min</c>–<c>Max</c> range.
    /// </summary>
    public double? Value { get; set; }

    /// <summary>The low end of the range (default 0).</summary>
    public double? Min { get; set; }

    /// <summary>The high end of the range (default 1).</summary>
    public double? Max { get; set; }

    /// <summary>The upper bound of what counts as the low segment of the range.</summary>
    public double? Low { get; set; }

    /// <summary>The lower bound of what counts as the high segment of the range.</summary>
    public double? High { get; set; }

    /// <summary>
    ///     Where the ideal value sits, which tells the browser which segments count as good — and so how to
    ///     colour the gauge.
    /// </summary>
    public double? Optimum { get; set; }

    /// <summary>The <c>id</c> of the form this meter belongs to.</summary>
    public new string? Form { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Value is not null)
        {
            AppendAttr(sb, "value", Value.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Min is not null)
        {
            AppendAttr(sb, "min", Min.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Max is not null)
        {
            AppendAttr(sb, "max", Max.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Low is not null)
        {
            AppendAttr(sb, "low", Low.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (High is not null)
        {
            AppendAttr(sb, "high", High.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Optimum is not null)
        {
            AppendAttr(sb, "optimum", Optimum.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }
    }
}
