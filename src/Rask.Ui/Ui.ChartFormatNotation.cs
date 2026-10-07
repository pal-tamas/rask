namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiChartFormat" /> shortens a number. <c>Intl.NumberFormat</c>'s <c>notation</c>.</summary>
    public enum ChartFormatNotation
    {
        /// <summary>Every digit.</summary>
        Standard = 0,

        /// <summary>1.2K, 3M.</summary>
        Compact,

        /// <summary>1.235E8.</summary>
        Scientific,
    }
}
