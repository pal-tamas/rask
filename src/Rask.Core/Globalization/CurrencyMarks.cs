using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Rask.Core.Globalization;

/// <summary>
///     How a culture writes an amount of money, for <see cref="Format.Money(decimal)" />: its currency's sign and code,
///     and the patterns an amount of another currency is written in, its code standing in for the sign.
/// </summary>
internal static class CurrencyMarks
{
    // A neutral culture's country's culture ("hu" → "hu-HU"), and the currency of each culture asked of, by name.
    private static readonly ConcurrentDictionary<string, CultureInfo> Countries = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> Currencies = new(StringComparer.Ordinal);

    // A read-only culture's patterns with another currency's code in its sign's place, made once per pair.
    private static readonly ConditionalWeakTable<NumberFormatInfo, ConcurrentDictionary<string, NumberFormatInfo>> Resigned = new();

    /// <summary>
    ///     The culture an amount is written in: <paramref name="culture" />, or for a language alone its main country's,
    ///     since a neutral culture names no currency and writes <c>¤</c>.
    /// </summary>
    internal static CultureInfo Of(CultureInfo culture)
    {
        if (!culture.IsNeutralCulture)
        {
            return culture;
        }

        return Countries.GetOrAdd(culture.Name, static name =>
        {
            var country = CultureInfo.CreateSpecificCulture(name);
            // With no culture data a language has no country, and the invariant culture is all there is.
            return country.Name.Length == 0 ? CultureInfo.GetCultureInfo(name) : CultureInfo.GetCultureInfo(country.Name);
        });
    }

    /// <summary><paramref name="currency" /> as an ISO 4217 code: three letters, upper case.</summary>
    internal static string Code(string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                $"\"{currency}\" is no ISO 4217 currency code. Name a currency in its three letters: Format.Money(amount, \"EUR\").",
                nameof(currency));
        }

        return currency.ToUpperInvariant();
    }

    /// <summary>
    ///     What <paramref name="culture" /> writes <paramref name="code" /> as: its own currency's sign (HUF is
    ///     <c>Ft</c> in Hungarian), and any other currency's code, as .NET knows no other currency's sign in it.
    /// </summary>
    internal static string Sign(CultureInfo culture, string code) =>
        string.Equals(CurrencyOf(culture), code, StringComparison.Ordinal) ? culture.NumberFormat.CurrencySymbol : code;

    /// <summary>
    ///     <paramref name="format" /> writing <paramref name="sign" /> where its own currency's sign goes. A code is set
    ///     apart from the digits by a space where the culture writes its sign against them, as CLDR does:
    ///     <c>$1,200</c> but <c>EUR 1,200</c>.
    /// </summary>
    internal static NumberFormatInfo Signed(NumberFormatInfo format, string sign)
    {
        if (string.Equals(format.CurrencySymbol, sign, StringComparison.Ordinal))
        {
            return format;
        }

        return format.IsReadOnly
            ? Resigned.GetOrCreateValue(format).GetOrAdd(sign, static (code, own) => Resign(own, code), format)
            : Resign(format, sign);
    }

    private static NumberFormatInfo Resign(NumberFormatInfo format, string code)
    {
        var resigned = (NumberFormatInfo)format.Clone();
        resigned.CurrencySymbol = code;
        resigned.CurrencyPositivePattern = format.CurrencyPositivePattern switch
        {
            0 => 2, // $n → $ n
            1 => 3, // n$ → n $
            _ => format.CurrencyPositivePattern,
        };
        resigned.CurrencyNegativePattern = format.CurrencyNegativePattern switch
        {
            0 => 14, // ($n) → ($ n)
            1 => 9, // -$n → -$ n
            2 => 12, // $-n → $ -n
            3 => 11, // $n- → $ n-
            4 => 15, // (n$) → (n $)
            5 => 8, // -n$ → -n $
            6 => 13, // n-$ → n- $
            7 => 10, // n$- → n $-
            _ => format.CurrencyNegativePattern,
        };
        return NumberFormatInfo.ReadOnly(resigned);
    }

    // The ISO 4217 code of the culture's country's currency; none for the invariant culture, which has no country.
    private static string? CurrencyOf(CultureInfo culture) =>
        culture.Name.Length == 0 || culture.IsNeutralCulture ? null : Currencies.GetOrAdd(culture.Name, RegionCurrency);

    private static string RegionCurrency(string culture)
    {
        try
        {
            return new RegionInfo(culture).ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            // A culture of no one country — "en-001", the world's English — has no currency of its own.
            return "";
        }
    }
}
