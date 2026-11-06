using System.Globalization;
using System.Text;
using static Rask.Markup;

namespace Rask;

/// <summary>
///     A typed date's parts — month, day and year as the runtime's <c>data-rask-segments</c> reads them — for the
///     date picker's input and for the inputs a calendar draws over its months.
/// </summary>
internal static class UiDateParts
{
    // The locale's own order and the text between the parts: mm/dd/yyyy in the US, dd.mm.yyyy in Germany,
    // yyyy/mm/dd in Japan, and Hungary's yyyy. mm. dd. with the full stop it ends on.
    internal static IEnumerable<Component> Of(CultureInfo culture, bool disabled)
    {
        var format = culture.DateTimeFormat;
        var (order, between) = Read(format.ShortDatePattern, format.DateSeparator);

        for (var i = 0; i < order.Length; i++)
        {
            if (i > 0)
            {
                yield return Span.Key("sep-" + order[i])[Kept(between[i].Length == 0 ? format.DateSeparator : between[i])];
            }

            yield return Part(order[i], disabled);
        }

        if (between[^1].Length > 0)
        {
            yield return Span.Key("sep-end")[Kept(between[^1])];
        }
    }

    // A space between the parts is kept where the row of parts would collapse it (Hungary's "2026. 11. 06.").
    private static string Kept(string text) => text.Replace(' ', '\u00A0');

    /// <summary>
    ///     A short date pattern's parts in order, and the text before each and after the last (<c>Between[i]</c>
    ///     before part <c>i</c>, the last entry after them all): a <c>/</c> is the culture's separator and a quoted
    ///     run its own text.
    /// </summary>
    internal static (string Order, string[] Between) Read(string pattern, string separator)
    {
        var order = new StringBuilder(3);
        var between = new List<string>(4);
        var text = new StringBuilder();
        var quoted = false;

        foreach (var c in pattern)
        {
            if (c is '\'' or '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is 'M' or 'd' or 'y')
            {
                // A part's further letters, or a part the pattern names twice, add nothing.
                if (order.ToString().Contains(c, StringComparison.Ordinal))
                {
                    continue;
                }

                between.Add(text.ToString());
                text.Clear();
                order.Append(c);
            }
            else if (!quoted && c == '/')
            {
                text.Append(separator);
            }
            else
            {
                text.Append(c);
            }
        }

        between.Add(text.ToString());
        return (order.ToString(), [.. between]);
    }

    // A part is the browser's own: no value and no handler, so a render never writes over what is being typed.
    private static HTMLInputElement<string> Part(char part, bool disabled)
    {
        var (name, kind, placeholder) = part switch
        {
            'M' => (RaskStrings.Get(RaskString.DatePickerMonth, "Month"), "month", RaskStrings.Get(RaskString.DatePickerMonthPlaceholder, "mm")),
            'd' => (RaskStrings.Get(RaskString.DatePickerDay, "Day"), "day", RaskStrings.Get(RaskString.DatePickerDayPlaceholder, "dd")),
            _ => (RaskStrings.Get(RaskString.DatePickerYear, "Year"), "year", RaskStrings.Get(RaskString.DatePickerYearPlaceholder, "yyyy")),
        };

        return Input.Of<string>()
            .Key(part)
            .Type(InputType.Text)
            .Class(part == 'y' ? UiDatePickerLook.SegmentYear : UiDatePickerLook.Segment)
            .Data(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-" + kind + "-input"] = "", ["rask-segment"] = kind })
            .Disabled(disabled)
            .Placeholder(placeholder)
            .Aria("label", name)
            .Attributes(("inputmode", "numeric"));
    }
}
