namespace Rask.Ui;

/// <summary>
/// What each row of a <see cref="UiChart{T}" /> is called — built by the chart's <c>X</c>, never written by name.
/// </summary>
/// <remarks>
/// The value is written in the reader's culture: a <c>DateOnly</c> as their short date, a number with their
/// separators. Return a string for anything else — <c>c.X(s =&gt; s.Month.ToString("MMM"))</c>.
/// </remarks>
public sealed partial class UiChartAxis : Component
{
    /// <inheritdoc />
    protected override Component? Render() => null;
}
