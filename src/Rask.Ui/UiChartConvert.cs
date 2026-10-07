using System.Globalization;

namespace Rask;

/// <summary>How a value of <typeparamref name="T" /> becomes the number or the moment a chart plots.</summary>
/// <remarks>Decided once per type, so reading a point boxes nothing for the types a model actually holds.</remarks>
internal static class UiChartConvert<T>
{
#pragma warning disable S2743 // one converter per closed type is the point: it is what spares a lookup per value read
    internal static readonly Func<T, double> Number = CreateNumber();

    internal static readonly Func<T, DateTime> Time = CreateTime();

    internal static readonly UiChartFieldKind Kind = Classify();
#pragma warning restore S2743

    private static Func<T, double> CreateNumber() =>
        Typed<double>(v => v) ?? Typed<int>(v => v) ?? Typed<decimal>(v => (double)v) ?? Typed<long>(v => v)
        ?? Typed<float>(v => v) ?? Typed<short>(v => v) ?? Typed<byte>(v => v)
        ?? Typed<double?>(v => v ?? double.NaN) ?? Typed<int?>(v => v ?? double.NaN)
        ?? Typed<decimal?>(v => v is { } d ? (double)d : double.NaN) ?? Typed<long?>(v => v ?? double.NaN)
        ?? (v => v is IConvertible and not string and not DateTime
            ? Convert.ToDouble(v, CultureInfo.InvariantCulture)
            : double.NaN);

    // Wall-clock time, as Flux reads a date: no zone is applied, so a row plots at the hour it says.
    private static Func<T, DateTime> CreateTime() =>
        TypedTime<DateTime>(v => v) ?? TypedTime<DateOnly>(v => v.ToDateTime(TimeOnly.MinValue))
        ?? TypedTime<DateTimeOffset>(v => v.DateTime) ?? TypedTime<DateTime?>(v => v ?? default)
        ?? TypedTime<DateOnly?>(v => v?.ToDateTime(TimeOnly.MinValue) ?? default)
        ?? TypedTime<DateTimeOffset?>(v => v?.DateTime ?? default)
        ?? (_ => default);

    private static UiChartFieldKind Classify()
    {
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(DateTimeOffset))
        {
            return UiChartFieldKind.Time;
        }

        return type.IsPrimitive && type != typeof(bool) && type != typeof(char) || type == typeof(decimal)
            ? UiChartFieldKind.Number
            : UiChartFieldKind.Text;
    }

    private static Func<T, double>? Typed<TKnown>(Func<TKnown, double> read) => read as Func<T, double>;

    private static Func<T, DateTime>? TypedTime<TKnown>(Func<TKnown, DateTime> read) => read as Func<T, DateTime>;
}
