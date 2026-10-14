using System.Globalization;

namespace Rask.Workbooks;

/// <summary>What a cell holds, said in the workbook's own terms: a day, a moment or a number - or a clear refusal.</summary>
internal static class CellValue
{
    /// <summary>
    ///     A raw <see cref="DateTime" />, <see cref="DateOnly" /> or <see cref="DateTimeOffset" /> is a day (at midnight) or a moment,
    ///     read as the clock on the wall - as <see cref="Xlsx.Moment" /> and <see cref="Xlsx.Day" /> are. Anything else is as it came.
    /// </summary>
    public static object? Normalize(object? value) => value switch
    {
        DateTime moment => moment.TimeOfDay == TimeSpan.Zero ? new Xlsx.Day(moment) : new Xlsx.Moment(moment),
        DateOnly day => new Xlsx.Day(day.ToDateTime(TimeOnly.MinValue)),
        DateTimeOffset offset => offset.TimeOfDay == TimeSpan.Zero ? new Xlsx.Day(offset.DateTime) : new Xlsx.Moment(offset.DateTime),
        _ => value,
    };

    /// <summary>Whether Excel can hold this as a number; a finite float or an integer, never a <see cref="Guid" /> or a <see cref="TimeSpan" />.</summary>
    public static bool IsNumber(object value) => value switch
    {
        sbyte or byte or short or ushort or int or uint or long or ulong => true,
        float single => float.IsFinite(single),
        double number => double.IsFinite(number),
        _ => false,
    };

    public static string Invariant(object number) => ((IFormattable)number).ToString(null, CultureInfo.InvariantCulture);

    // A decimal is written without the zeros it ends in: 3485, not 3485.0000.
    public static string Invariant(decimal number) => (number / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    // Excel counts days from the end of 1899; the time of day is the fraction.
    public static string Serial(DateTime wallClock) =>
        (wallClock - new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified)).TotalDays.ToString("R", CultureInfo.InvariantCulture);

    public static ArgumentException Refuse(object value) => new(
        value is double or float
            ? "A cell cannot hold NaN or an infinity; Excel has no such number."
            : $"A cell cannot hold a {value.GetType().Name}: give it text, a number, a bool, a DateTime or a DateOnly (or Xlsx.Day, Xlsx.Moment).",
        nameof(value));
}
