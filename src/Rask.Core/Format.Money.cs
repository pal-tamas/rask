using System.Globalization;
using Rask.Core.Globalization;

namespace Rask;

public static partial class Format
{
    // "C0", "C1" and "C2": an amount to as many places as it has, of the two money is written to.
    private static readonly string[] MoneyPlaces = ["C0", "C1", "C2"];

    /// <summary>
    ///     An amount of the visitor's own currency, where and how the culture writes one, to at most two places and with
    ///     none of a column's padding zeros: <c>1 200 Ft</c> and <c>1 764,6 Ft</c> in Hungarian, <c>$1,200</c> and
    ///     <c>$1,764.6</c> in English.
    /// </summary>
    /// <remarks>
    ///     The currency is the culture's country's: a language alone (<c>"hu"</c>, <c>"en"</c>) is read as its main
    ///     country's (Hungary, the United States), as a neutral culture names no currency. A runtime with no culture data
    ///     — a browser app that ships no ICU — writes English as US English (<c>$</c>), and any other language's
    ///     currency as <c>¤</c>; name it with <see cref="Money(decimal, string)" /> there.
    /// </remarks>
    /// <param name="amount">The amount, rounded half away from zero to two places.</param>
    /// <returns>The amount as the visitor writes one.</returns>
    public static string Money(decimal amount)
    {
        var culture = FormattingCulture.Of(Culture);
        return Money(amount, culture.NumberFormat, culture.NumberFormat.CurrencySymbol);
    }

    /// <summary>
    ///     An amount of <paramref name="currency" />, where and how the culture writes one, to at most two places:
    ///     <c>Format.Money(1764.6m, "EUR")</c> is <c>1 764,6 EUR</c> in Hungarian and <c>EUR 1,764.6</c> in English.
    /// </summary>
    /// <remarks>
    ///     The culture's own currency is written in its sign — HUF as <c>Ft</c> in Hungarian, USD as <c>$</c> in
    ///     English — and any other in its code, as .NET knows no other currency's sign in a culture.
    /// </remarks>
    /// <param name="amount">The amount, rounded half away from zero to two places.</param>
    /// <param name="currency">The currency's ISO 4217 code: <c>"EUR"</c>, <c>"HUF"</c>.</param>
    /// <returns>The amount as the visitor writes one.</returns>
    public static string Money(decimal amount, string currency)
    {
        var code = CurrencyMarks.Code(currency);
        var culture = FormattingCulture.Of(Culture);
        return Money(amount, culture.NumberFormat, CurrencyMarks.Sign(culture, code));
    }

    private static string Money(decimal amount, NumberFormatInfo format, string sign)
    {
        var rounded = Math.Round(amount, MoneyPlaces.Length - 1, MidpointRounding.AwayFromZero);
        return rounded.ToString(MoneyPlaces[PlacesIn(rounded)], CurrencyMarks.Signed(format, sign));
    }

    // How many places a number rounded to two has: 0 for 1200.00, 1 for 1200.50, 2 for 1200.05.
    private static int PlacesIn(decimal rounded)
    {
        var places = 0;
        for (var at = rounded; places < MoneyPlaces.Length - 1 && at != decimal.Truncate(at); at *= 10)
        {
            places++;
        }

        return places;
    }
}
