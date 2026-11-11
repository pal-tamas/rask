using System.Globalization;

namespace Rask.Workbooks;

/// <summary>A read cell as heading text: a text as it is, anything else as it reads in any culture (<c>2026</c>, <c>2026-10-14</c>).</summary>
internal static class CellText
{
    public static string Of(object? value) => value switch
    {
        null => "",
        string text => text,
        bool flag => flag ? "TRUE" : "FALSE",
        Xlsx.Day day => day.WallClock.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Xlsx.Moment moment => moment.WallClock.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
