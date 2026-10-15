using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Rask;

/// <summary>
///     What a calendar tells the runtime that draws it from the first paint on (<c>rask-calendar.ts</c>): the
///     mode, the limits, the week, the culture's names and patterns, and the classes each state is drawn with —
///     as one JSON object in <c>data-rask-calendar</c>.
/// </summary>
internal static class UiCalendarHook
{
    /// <summary>The attribute that names a part.</summary>
    internal const string Part = "data-rask-calendar-part";

    /// <summary>A bound field whose value waits for the reader's next action, and goes ahead of it.</summary>
    internal static readonly (string, string?)[] WaitsForAction = [("data-rask-bind-on", "action")];

    /// <summary>The pair that names a part, for <c>Attributes(…)</c>.</summary>
    internal static (string, string?) Named(string part) => (Part, part);

    /// <summary>The configuration, for <c>data-rask-calendar</c>.</summary>
    /// <param name="options">The calendar's props, resolved.</param>
    /// <param name="mode">The picks' mode.</param>
    /// <param name="picker">The date picker it sits in, or <c>null</c> for a calendar on its own.</param>
    internal static string Config(UiCalendarOptions options, string mode, Picker? picker = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("mode", mode);
            json.WriteString("today", UiCalendarValue.Day(options.Today));
            Day(json, "min", options.Min);
            Day(json, "max", options.Max);
            Day(json, "openTo", options.OpenTo);
            if (options.Unavailable is { Count: > 0 } unavailable)
            {
                json.WriteStartArray("unavailable");
                foreach (var day in unavailable)
                {
                    json.WriteStringValue(UiCalendarValue.Day(day));
                }

                json.WriteEndArray();
            }

            Number(json, "minRange", options.MinRange);
            Number(json, "maxRange", options.MaxRange);
            json.WriteNumber("startDay", (int)options.StartDay);
            json.WriteNumber("months", options.Months);
            Flag(json, "forceOpenTo", options.ForceOpenTo);
            Flag(json, "fixedWeeks", options.FixedWeeks);
            Flag(json, "weekNumbers", options.WeekNumbers);
            Flag(json, "static", options.Static);
            Flag(json, "selectableHeader", options.SelectableHeader);
            json.WriteString("previous", RaskStrings.Get(RaskString.PickerPreviousMonth, "Previous month"));
            json.WriteString("next", RaskStrings.Get(RaskString.PickerNextMonth, "Next month"));
            if (picker is { } p)
            {
                json.WriteBoolean("picker", true);
                Flag(json, "confirms", p.Confirms);
                json.WriteString("placeholder", p.Placeholder);
                json.WriteString("separator", UiDatePickerText.Separator);
            }

            Culture(json, options.Culture);
            Look(json, options);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>What a date picker adds to its calendar's configuration.</summary>
    /// <param name="Confirms">Whether a choice waits for the popup's confirming button.</param>
    /// <param name="Placeholder">What the button says while nothing is chosen.</param>
    internal readonly record struct Picker(bool Confirms, string Placeholder);

    private static void Culture(Utf8JsonWriter json, CultureInfo culture)
    {
        var names = culture.DateTimeFormat;
        json.WriteStartObject("culture");
        Names(json, "months", names.MonthNames);
        Names(json, "monthsGenitive", names.MonthGenitiveNames);
        Names(json, "monthsAbbreviated", names.AbbreviatedMonthNames);
        Names(json, "monthsAbbreviatedGenitive", names.AbbreviatedMonthGenitiveNames);
        Names(json, "days", names.DayNames);
        Names(json, "daysAbbreviated", names.AbbreviatedDayNames);
        json.WriteString("long", names.LongDatePattern);
        json.WriteString("yearMonth", names.YearMonthPattern);
        json.WriteString("medium", UiDatePickerText.MediumPattern(culture));
        json.WriteEndObject();
    }

    private static void Look(Utf8JsonWriter json, UiCalendarOptions options)
    {
        json.WriteStartObject("look");
        json.WriteString("cell", options.WeekNumbers ? UiCalendarLook.CellBesideWeek(options.Size) : UiCalendarLook.Cell(options.Size));
        json.WriteString("td", UiCalendarLook.TdBase);
        json.WriteString("first", UiCalendarLook.TdFirst);
        json.WriteString("last", UiCalendarLook.TdLast);
        json.WriteString("inRange", UiCalendarLook.TdInRange);
        json.WriteString("outside", UiCalendarLook.TdOutside);
        json.WriteString("unavailable", UiCalendarLook.TdUnavailable);
        json.WriteString("day", UiCalendarLook.Day);
        json.WriteString("daySelected", UiCalendarLook.DaySelected);
        json.WriteString("dayStatic", UiCalendarLook.DayStatic);
        json.WriteString("dayStaticSelected", UiCalendarLook.DayStaticSelected);
        json.WriteString("dayText", UiCalendarLook.DayText);
        json.WriteString("tooltipHost", UiCalendarLook.TooltipHost);
        json.WriteString("todayDot", UiCalendarLook.TodayDot);
        json.WriteString("todayDotHidden", UiCalendarLook.TodayDotHidden);
        json.WriteString("staticDot", UiCalendarLook.StaticDot);
        json.WriteString("staticDotHidden", UiCalendarLook.StaticDotHidden);
        json.WriteString("dot", UiCalendarLook.Dot);
        json.WriteString("dotSelected", UiCalendarLook.DotSelected);
        json.WriteString("heading", UiCalendarLook.Heading);
        json.WriteString("headingHidden", UiCalendarLook.HeadingHidden);
        json.WriteString("headingText", UiCalendarLook.HeadingText);
        json.WriteString("folded", UiCalendarLook.MonthFolded);
        json.WriteString("row", UiCalendarLook.Row);
        json.WriteString("rowBelow", UiCalendarLook.RowBelow);
        json.WriteString("weekNumber", UiCalendarLook.WeekNumber);
        json.WriteString("preset", UiDatePickerLook.Preset);
        json.WriteString("presetChecked", UiDatePickerLook.PresetChecked);
        json.WriteString("placeholder", UiDatePickerLook.Placeholder);
        json.WriteEndObject();
    }

    private static void Names(Utf8JsonWriter json, string name, string[] names)
    {
        json.WriteStartArray(name);
        foreach (var one in names)
        {
            json.WriteStringValue(one);
        }

        json.WriteEndArray();
    }

    private static void Day(Utf8JsonWriter json, string name, DateOnly? day)
    {
        if (day is { } d)
        {
            json.WriteString(name, UiCalendarValue.Day(d));
        }
    }

    private static void Number(Utf8JsonWriter json, string name, int? number)
    {
        if (number is { } n)
        {
            json.WriteNumber(name, n);
        }
    }

    private static void Flag(Utf8JsonWriter json, string name, bool on)
    {
        if (on)
        {
            json.WriteBoolean(name, true);
        }
    }
}
