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
public static class Format
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
    ///     A day and a time to the minute, in the culture's short date and time: <c>2026. 10. 09. 14:35</c> in Hungarian,
    ///     <c>10/9/2026 2:35 PM</c> in English.
    /// </summary>
    /// <param name="moment">The moment — a UTC one is first put on the wall clock.</param>
    /// <returns>The moment as the visitor writes one.</returns>
    public static string Moment(DateTime moment) => AmbientClock.OnTheWall(moment).ToString("g", Culture);

    /// <inheritdoc cref="Moment(DateTime)" />
    public static string Moment(DateTimeOffset moment) => AmbientClock.OnTheWall(moment).ToString("g", Culture);

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

    private static CultureInfo Culture => RaskCulture.Current;

    private static string Places(int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        return "N" + decimals.ToString(CultureInfo.InvariantCulture);
    }
}
