namespace Rask;

public static partial class Ui
{
    /// <summary>How much a <see cref="UiChartFormat" /> date or time style says. <c>Intl.DateTimeFormat</c>'s <c>dateStyle</c> and <c>timeStyle</c>.</summary>
#pragma warning disable CA1720 // 'Long' and 'Short' are Intl's own values for dateStyle and timeStyle, which this mirrors
    public enum ChartFormatLength
    {
        /// <summary>Friday, March 15, 2024.</summary>
        Full = 0,

        /// <summary>March 15, 2024.</summary>
        Long,

        /// <summary>Mar 15, 2024.</summary>
        Medium,

        /// <summary>3/15/24.</summary>
        Short,
    }
#pragma warning restore CA1720
}
