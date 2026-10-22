using System.Globalization;
using static Rask.Markup;

namespace Rask;

/// <summary>
///     A typed date's parts — month, day and year as the runtime's <c>data-rask-segments</c> reads them — for the
///     date picker's input and for the inputs a calendar draws over its months.
/// </summary>
internal static class UiDateParts
{
    // The locale's own order and separator: mm/dd/yyyy in the US, dd.mm.yyyy in Germany, yyyy/mm/dd in Japan.
    internal static IEnumerable<Component> Of(CultureInfo culture, bool disabled)
    {
        var format = culture.DateTimeFormat;
        var order = string.Concat(format.ShortDatePattern.Where(c => c is 'M' or 'd' or 'y').Distinct());
        var first = true;

        foreach (var part in order)
        {
            if (!first)
            {
                yield return Span.Key("sep-" + part)[format.DateSeparator];
            }

            first = false;
            yield return Part(part, disabled);
        }
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
