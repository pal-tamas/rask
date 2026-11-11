using System.Globalization;

namespace Rask.Workbooks;

/// <summary>
///     A cell's value as the writer's own types: a text is a <see cref="string" />, a number a <see cref="decimal" />,
///     a flag a <see cref="bool" />, a number styled as a date an <see cref="Xlsx.Day" /> or an <see cref="Xlsx.Moment" />,
///     an empty cell <see langword="null" />. A formula is what Excel last worked it out to; an error its text (<c>#N/A</c>).
/// </summary>
internal sealed class CellDecoder(IReadOnlyList<string> shared, CellStyles styles, bool date1904)
{
    public object? Decode(string? type, string? style, string? value, string? inline, string at) => type switch
    {
        "s" => Shared(value, at),
        "inlineStr" => inline ?? value,
        "str" or "e" => value,
        "b" => value is null ? null : value is "1" or "true",
        "d" => value is null ? null : Moment(Iso(value, at), Kind(style)),
        null or "n" => value is null ? null : Number(value, Kind(style), at),
        _ => throw Package.Malformed($"cell {at} is of a type Excel does not write ('{type}')"),
    };

    private static object Moment(DateTime wallClock, DateKind kind) =>
        kind == DateKind.Day || (kind == DateKind.None && wallClock.TimeOfDay == TimeSpan.Zero) ? new Xlsx.Day(wallClock) : new Xlsx.Moment(wallClock);

    private static DateTime Iso(string value, string at) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock)
            ? DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified)
            : throw Package.Malformed($"cell {at} holds '{value}', which is not a date");

    private object Number(string value, DateKind kind, string at)
    {
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            // beyond what a decimal holds (1E+300): the double Excel wrote
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var huge)
                ? huge
                : throw Package.Malformed($"cell {at} holds '{value}', which is not a number");
        }

        return kind != DateKind.None && ExcelSerial.ToWallClock(number, date1904) is { } wallClock ? Moment(wallClock, kind) : number;
    }

    private string Shared(string? value, string at) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < shared.Count
            ? shared[index]
            : throw Package.Malformed($"cell {at} names shared string '{value}', which the workbook does not hold");

    private DateKind Kind(string? style) =>
        int.TryParse(style, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? styles.Kind(index) : DateKind.None;
}
