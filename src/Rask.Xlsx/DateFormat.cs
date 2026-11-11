using System.Buffers;

namespace Rask.Workbooks;

/// <summary>
///     Whether a number format shows a date: Excel's built-in date and time formats (14-22, 45-47), or a format of its
///     own with a day, month, year, hour or second in it - outside quotes, escapes and <c>[brackets]</c>.
/// </summary>
internal static class DateFormat
{
    private static readonly SearchValues<char> ElapsedUnits = SearchValues.Create("hHmMsS");

    public static DateKind Of(int id, string? code) => code is not null ? OfCode(code) : OfBuiltIn(id);

    private static DateKind OfBuiltIn(int id) => id switch
    {
        >= 14 and <= 17 => DateKind.Day,
        (>= 18 and <= 22) or (>= 45 and <= 47) => DateKind.Moment,
        _ => DateKind.None,
    };

    // only the first section counts: the one a positive number is shown in
    private static DateKind OfCode(string code)
    {
        var date = false;
        var time = false;
        var at = 0;
        while (at < code.Length && code[at] != ';')
        {
            switch (code[at])
            {
                case '"':
                    at = After(code, at, '"');
                    break;
                case '[':
                    var close = After(code, at, ']');
                    time |= IsElapsed(code.AsSpan(at + 1, Math.Max(0, close - at - 2)));
                    at = close;
                    break;
                case '\\' or '_' or '*':
                    at += 2; // the next character is shown, padded or repeated as it is
                    break;
                default:
                    date |= code[at] is 'd' or 'D' or 'y' or 'Y' or 'm' or 'M';
                    time |= code[at] is 'h' or 'H' or 's' or 'S';
                    at++;
                    break;
            }
        }

        if (time)
        {
            return DateKind.Moment;
        }

        return date ? DateKind.Day : DateKind.None;
    }

    // the place after the character that closes what opens at `at`
    private static int After(string code, int at, char close)
    {
        var end = code.IndexOf(close, at + 1);
        return end < 0 ? code.Length : end + 1;
    }

    // [h], [mm], [ss]: a time elapsed
    private static bool IsElapsed(ReadOnlySpan<char> inside) => !inside.IsEmpty && !inside.ContainsAnyExcept(ElapsedUnits);
}
