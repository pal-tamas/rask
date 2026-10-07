namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiChartLine" /> or <see cref="UiChartArea" /> joins its points.</summary>
    public enum ChartCurve
    {
        /// <summary>A curve through every point that never overshoots one.</summary>
        Smooth = 0,

        /// <summary>Straight segments.</summary>
        None,
    }
}
