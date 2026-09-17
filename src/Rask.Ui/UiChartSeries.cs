namespace Rask.Ui;

/// <summary>
/// One series of a <see cref="UiChart{T}" /> — built by the chart's <c>Line</c>, <c>Area</c> or <c>Bar</c>, never
/// written by name.
/// </summary>
/// <remarks>
/// It renders nothing itself: the chart keeps what each series reads from a row, and draws every series on one set
/// of axes. It is a component so that it has a chain for <see cref="Label" /> and <see cref="Tone" />, and so that
/// one arm of a conditional can be <see langword="null" />. Not generic, and that is deliberate: the chart already
/// knows the row type, and a series that had to be told it again would need <c>Of&lt;T&gt;()</c> to be built at all.
/// </remarks>
public sealed partial class UiChartSeries : Component
{
    /// <summary>What the series is called — in the legend, the tooltips and the table. "Series 1" and on otherwise.</summary>
    public string? Label { get; set; }

    /// <summary>Its colour. The next in turn — primary, secondary, accent, … — unless this says otherwise.</summary>
    public UiTone? Tone { get; set; }

    /// <summary>How it is drawn. Set by the chart's <c>Line</c>, <c>Area</c> or <c>Bar</c>.</summary>
    public UiChartKind? Kind { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => null;
}
