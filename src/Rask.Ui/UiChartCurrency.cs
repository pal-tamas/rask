using System.Collections.Frozen;
using System.Globalization;

namespace Rask;

/// <summary>
///     Where a culture puts a currency's sign and how many decimals the currency has: Intl's
///     <c>style: "currency"</c>, from .NET's own currency patterns and ISO 4217's minor units.
/// </summary>
internal static class UiChartCurrency
{
    // ISO 4217 currencies with no minor unit, and those with three; the rest have two.
    private static readonly FrozenSet<string> NoDecimals = new[]
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF",
        "XOF", "XPF",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ThreeDecimals = new[] { "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND" }
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>How many decimals <paramref name="code" /> is written with; the culture's own with no code.</summary>
    internal static int Decimals(string? code, CultureInfo culture)
    {
        if (code is null)
        {
            return culture.NumberFormat.CurrencyDecimalDigits;
        }

        var upper = code.ToUpperInvariant();
        if (NoDecimals.Contains(upper))
        {
            return 0;
        }

        return ThreeDecimals.Contains(upper) ? 3 : 2;
    }

    /// <summary>
    ///     <paramref name="magnitude" /> (the number without its sign, already written) with the sign and the
    ///     currency where <paramref name="culture" /> puts them: "$1,234.56" in English, "12 345,00 Ft" in Hungarian.
    /// </summary>
    internal static string Place(string magnitude, bool negative, string? code, CultureInfo culture)
    {
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = Symbol(code, culture);
        var shape = (negative ? -1d : 1d).ToString("C0", format);
        var one = shape.IndexOf('1', StringComparison.Ordinal);
        return string.Concat(shape.AsSpan(0, one), magnitude, shape.AsSpan(one + 1));
    }

    // The culture's own sign for its own currency, the familiar sign of a few, and the code itself for the rest —
    // set off by a space where the culture writes its sign against the number.
    private static string Symbol(string? code, CultureInfo culture)
    {
        if (code is null || string.Equals(code, LocalCode(culture), StringComparison.OrdinalIgnoreCase))
        {
            return culture.NumberFormat.CurrencySymbol;
        }

        return code.ToUpperInvariant() switch
        {
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            "JPY" => "¥",
            var other => culture.NumberFormat.CurrencyPositivePattern == 0 ? other + " " : other,
        };
    }

    private static string? LocalCode(CultureInfo culture)
    {
        if (culture.Name.Length == 0)
        {
            return null;
        }

        try
        {
            return new RegionInfo(CultureInfo.CreateSpecificCulture(culture.Name).Name).ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            // A culture with no region (or none this runtime knows, as under invariant globalization).
            return null;
        }
    }
}
