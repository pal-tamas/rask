namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiChartSeries" /> is drawn.</summary>
    public enum ChartKind
    {
        /// <summary>A line through each row's value.</summary>
        Line = 0,

        /// <summary>A line with the area under it filled.</summary>
        Area,

        /// <summary>A bar per row.</summary>
        Bar,
    }
}
