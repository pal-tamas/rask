using System.Globalization;
using Rask.Core.Globalization;

namespace Rask;

/// <summary>
///     A value written for a person to read, in the visitor's culture: <c>Format.Day(order.Placed)</c>,
///     <c>Format.Moment(fueling.At)</c>, <c>Format.Number(litres)</c>. One place every page writes a day, a moment and a
///     number from, so no two pages write the same value differently.
/// </summary>
/// <remarks>
///     <para>
///         The culture is the visitor's — <c>AddRask(culture: "hu")</c> writes <c>2026. 10. 09.</c> and <c>24 000</c>,
///         English <c>10/9/2026</c> and <c>24,000</c> — and reading it marks the component being rendered as depending
///         on it, as <c>Culture</c> does, so a language switch repaints what it wrote.
///     </para>
///     <para>
///         A moment in UTC (a <see cref="DateTimeOffset" />, or a <see cref="DateTime" /> of kind
///         <see cref="DateTimeKind.Utc" />) is written as the wall clock showed it, in <see cref="Clock.Zone" />; an
///         unspecified <see cref="DateTime" /> is a wall-clock time already and is written as it is.
///     </para>
///     <para>
///         These are display formats. A value sent somewhere — a route value, a file another program reads — is written
///         with <see cref="CultureInfo.InvariantCulture" /> instead.
///     </para>
/// </remarks>
public static partial class Format
{
    /// <summary>A day, in the culture's short date: <c>2026. 10. 09.</c> in Hungarian, <c>10/9/2026</c> in English.</summary>
    /// <param name="day">The day.</param>
    /// <returns>The day as the visitor writes one.</returns>
    public static string Day(DateOnly day) => day.ToString("d", Culture);

    /// <summary>The day a moment falls on, on the wall clock, in the culture's short date: <c>2026. 10. 09.</c>.</summary>
    /// <param name="moment">The moment — a UTC one is first put on the wall clock.</param>
    /// <returns>The day as the visitor writes one.</returns>
    public static string Day(DateTime moment) => Day(DateOnly.FromDateTime(AmbientClock.OnTheWall(moment)));

    /// <inheritdoc cref="Day(DateTime)" />
    public static string Day(DateTimeOffset moment) => Day(DateOnly.FromDateTime(AmbientClock.OnTheWall(moment)));

    /// <summary>
    ///     A day and a time to the minute, in the culture's short date and time with a 24-hour clock's hour always in two
    ///     digits, so moments line up in a column: <c>2026. 10. 09. 09:05</c> in Hungarian, <c>10/9/2026 9:05 AM</c> in
    ///     English (a 12-hour clock is written as the culture writes it).
    /// </summary>
    /// <param name="moment">The moment — a UTC one is first put on the wall clock.</param>
    /// <returns>The moment as the visitor writes one.</returns>
    public static string Moment(DateTime moment) => AmbientClock.OnTheWall(moment).ToString(MomentPattern(Culture), Culture);

    /// <inheritdoc cref="Moment(DateTime)" />
    public static string Moment(DateTimeOffset moment) => AmbientClock.OnTheWall(moment).ToString(MomentPattern(Culture), Culture);

    /// <summary>A whole number with its thousands apart: <c>24 000</c> in Hungarian, <c>24,000</c> in English.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The number as the visitor writes one.</returns>
    public static string Number(long value) => value.ToString("N0", Culture);

    /// <summary>
    ///     A number with its thousands apart and as many decimals as it has — none of a column's padding zeros:
    ///     <c>1 764,6</c> in Hungarian, <c>1,764.6</c> in English.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The number as the visitor writes one.</returns>
    public static string Number(decimal value) => value.ToString("#,0.############################", Culture);

    /// <inheritdoc cref="Number(decimal)" />
    public static string Number(double value) => value.ToString("#,0.###############", Culture);

    /// <summary>A number to <paramref name="decimals" /> places, thousands apart: <c>Format.Number(1764.6m, 2)</c> is <c>1 764,60</c>.</summary>
    /// <param name="value">The number.</param>
    /// <param name="decimals">How many places after the decimal separator.</param>
    /// <returns>The number as the visitor writes one.</returns>
    public static string Number(decimal value, int decimals) => value.ToString(Places(decimals), Culture);

    /// <inheritdoc cref="Number(decimal, int)" />
    public static string Number(double value, int decimals) => value.ToString(Places(decimals), Culture);

    /// <summary>
    ///     A number to at most <paramref name="atMost" /> places, rounded half away from zero, thousands apart and with
    ///     none of a column's padding zeros: <c>Format.Number(1764.604m, atMost: 2)</c> is <c>1 764,6</c>, and 1200 is
    ///     <c>1 200</c>. Null writes every decimal the number has, as <see cref="Number(decimal)" /> does.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="atMost">The most places after the decimal separator, at most 15.</param>
    /// <returns>The number as the visitor writes one.</returns>
    public static string Number(decimal value, int? atMost) =>
        atMost is { } places
            ? Math.Round(value, Rounding(places), MidpointRounding.AwayFromZero).ToString(AtMost(places), Culture)
            : Number(value);

    /// <inheritdoc cref="Number(decimal, int?)" />
    public static string Number(double value, int? atMost) =>
        atMost is { } places
            ? Math.Round(value, Rounding(places), MidpointRounding.AwayFromZero).ToString(AtMost(places), Culture)
            : Number(value);

    private static CultureInfo Culture => RaskCulture.Current;

    /// <summary>
    ///     The culture's short date and short time (what <c>"g"</c> writes), with a 24-hour hour padded to two digits:
    ///     Hungarian's <c>H:mm</c> becomes <c>HH:mm</c>, so 9:05 and 14:35 stand in one column. A 12-hour pattern is kept.
    /// </summary>
    private static string MomentPattern(CultureInfo culture)
    {
        var format = culture.DateTimeFormat;
        var time = format.ShortTimePattern;
        if (time.Contains('H', StringComparison.Ordinal) && !time.Contains("HH", StringComparison.Ordinal))
        {
            time = time.Replace("H", "HH", StringComparison.Ordinal);
        }

        return format.ShortDatePattern + " " + time;
    }

    private static string Places(int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        return "N" + decimals.ToString(CultureInfo.InvariantCulture);
    }

    // "#,0.##" for two: as many of the places as the number has, and no more.
    private static string AtMost(int places) => places == 0 ? "#,0" : "#,0." + new string('#', places);

    // What Math.Round takes, for a decimal and a double alike.
    private static int Rounding(int places)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(places);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(places, 15);
        return places;
    }
}
