using System.Globalization;

namespace Rask.Core.Globalization;

/// <summary>A binary floating-point number as the decimal a person reads it as: the shortest one that round-trips.</summary>
/// <remarks>
///     <para>
///         Up to .NET 10 the <c>(decimal)</c> conversion rounded a <see cref="double" /> to 15 significant digits, so
///         <c>(decimal)0.3</c> was <c>0.3</c>. From .NET 11 it is exact: <c>0.2999999999999999888977697537</c>, the
///         binary value the literal stood for. A slider's step, a colour channel and a number rounded "half away from
///         zero" all mean the number as written, which is the shortest decimal that reads back as the same double.
///     </para>
///     <para>
///         A value no <see cref="decimal" /> holds (NaN, an infinity, beyond ±7.9e28) falls back to the plain
///         conversion, which throws as it always did.
///     </para>
/// </remarks>
internal static class ShortestDecimal
{
    /// <summary>The shortest decimal that round-trips to <paramref name="value" />.</summary>
    internal static decimal Of(double value) =>
        double.IsFinite(value)
        && decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var read)
            ? read
            : (decimal)value;

    /// <summary>The shortest decimal that round-trips to <paramref name="value" />, as a <see cref="float" />.</summary>
    internal static decimal Of(float value) =>
        float.IsFinite(value)
        && decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var read)
            ? read
            : (decimal)value;
}
