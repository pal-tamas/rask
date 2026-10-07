namespace Rask;

/// <summary>
/// The rows a <see cref="UiChart" /> draws. Made by <c>Ui.Chart.Value(rows)</c>, never by name.
/// </summary>
/// <remarks>
/// Flux hands its chart an array of rows and names a column with a string. A Rask chart holds the typed list
/// itself, and each part reads its number with a selector, so nothing is boxed and nothing is looked up by name.
/// </remarks>
public abstract class UiChartData
{
    private protected UiChartData()
    {
    }

    internal abstract int Count { get; }

    /// <summary>The row itself as a number: what a part with no field draws when the chart holds bare values.</summary>
    internal abstract double Number(int row);
}
