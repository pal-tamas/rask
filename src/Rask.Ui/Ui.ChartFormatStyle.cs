namespace Rask;

public static partial class Ui
{
    /// <summary>What kind of number a <see cref="UiChartFormat" /> writes. <c>Intl.NumberFormat</c>'s <c>style</c>.</summary>
#pragma warning disable CA1720 // 'Decimal' is Intl's own value for style, which this mirrors
    public enum ChartFormatStyle
    {
        /// <summary>A plain number.</summary>
        Decimal = 0,

        /// <summary>An amount of <see cref="UiChartFormat.Currency" />.</summary>
        Currency,

        /// <summary>A fraction as a percentage: 0.85 is 85%.</summary>
        Percent,

        /// <summary>A quantity of <see cref="UiChartFormat.Unit" />.</summary>
        Unit,
    }
#pragma warning restore CA1720
}
