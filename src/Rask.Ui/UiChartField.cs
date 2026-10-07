namespace Rask;

/// <summary>
/// What a chart part reads from a row. Made by a part's <c>Field(row =&gt; …)</c> step, never by name.
/// </summary>
public abstract class UiChartField
{
    private protected UiChartField()
    {
    }

    /// <summary>Whether the field holds dates, numbers or text: what decides the kind of axis it gets.</summary>
    internal abstract UiChartFieldKind Kind { get; }

    internal abstract double Number(UiChartData data, int row);

    internal abstract DateTime Time(UiChartData data, int row);

    internal abstract string Text(UiChartData data, int row);

    internal abstract object? Raw(UiChartData data, int row);
}
