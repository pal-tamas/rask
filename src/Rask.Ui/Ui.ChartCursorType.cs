namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiChartCursor" /> marks the row under the pointer.</summary>
    public enum ChartCursorType
    {
        /// <summary>A dashed line through the row.</summary>
        Line = 0,

        /// <summary>The row's band, shaded: for bars.</summary>
        Area,
    }
}
