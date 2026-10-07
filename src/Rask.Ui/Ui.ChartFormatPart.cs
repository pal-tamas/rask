namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiChartFormat" /> writes one part of a date. <c>Intl.DateTimeFormat</c>'s values.</summary>
#pragma warning disable CA1720 // 'Long' and 'Short' are Intl's own values for a date part, which this mirrors
    public enum ChartFormatPart
    {
        /// <summary>As a number: 3.</summary>
        Numeric = 0,

        /// <summary>As two digits: 03.</summary>
        TwoDigit,

        /// <summary>In full: March, Friday.</summary>
        Long,

        /// <summary>Abbreviated: Mar, Fri.</summary>
        Short,

        /// <summary>One letter: M, F.</summary>
        Narrow,
    }
#pragma warning restore CA1720
}
