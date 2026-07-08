using System.Globalization;

namespace Rask.Bootstrap;

// Stateless render + date-math helpers shared by BsDatePicker / BsTimePicker / BsDateTimePicker so the
// month grid, time columns, header and week math live in exactly one place (the pickers hold the state
// and pass it in). Everything is culture-driven: weekday order, names and the month label come from
// CultureInfo.DateTimeFormat, while the bound value round-trips invariant ISO in the picker itself.
internal static class PickerParts
{
    // Stable, collision-safe id for a day cell (targets aria-activedescendant on the trigger box).
    internal static string CellId(string prefix, DateOnly day) =>
        $"{prefix}-d-{day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

    // The first day of the week's culture order (Mon-first for hu-HU, Sun-first for en-US, …).
    internal static DateOnly WeekStart(DateOnly d, CultureInfo culture)
    {
        var firstDow = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)d.DayOfWeek - firstDow + 7) % 7;
        return d.AddDays(-offset);
    }

    internal static DateOnly WeekEnd(DateOnly d, CultureInfo culture) => WeekStart(d, culture).AddDays(6);

    // The localized weekday header row (role=row of 7 columnheaders), ordered from the culture's first day.
    internal static Component WeekdayHeaderRow(CultureInfo culture)
    {
        var dtf = culture.DateTimeFormat;
        var firstDow = (int)dtf.FirstDayOfWeek;
        var cells = new List<Component>(7);
        for (var i = 0; i < 7; i++)
        {
            var dow = (DayOfWeek)((firstDow + i) % 7);
            cells.Add(Div(
                Class: BsClass.Join("bs-cal-head", Txt.Muted),
                Role: "columnheader",
                Aria: new Dictionary<string, string?> { ["label"] = dtf.DayNames[(int)dow] },
                Key: i)[dtf.AbbreviatedDayNames[(int)dow]]);
        }

        return Div(Role: "row", Key: 0)[cells];
    }

    // The prev/next month header with the localized "MMMM yyyy" label.
    internal static Component MonthHeader(
        DateOnly view, CultureInfo culture, Callback onPrev, Callback onNext,
        bool prevDisabled, bool nextDisabled) =>
        Div(Class: BsClass.Join(Display.Flex(), Flex.Align(BsAlign.Center),
            Flex.Justify(BsJustify.Between), Margin.Bottom(2)))[
            Button(Type: "button", Class: "btn btn-sm btn-outline-secondary",
                Disabled: prevDisabled ? true : null,
                Aria: new Dictionary<string, string?> { ["label"] = "Previous month" },
                OnClick: prevDisabled ? null : onPrev)["‹"],
            Span(Class: Font.Semibold)[view.ToString("y", culture)],
            Button(Type: "button", Class: "btn btn-sm btn-outline-secondary",
                Disabled: nextDisabled ? true : null,
                Aria: new Dictionary<string, string?> { ["label"] = "Next month" },
                OnClick: nextDisabled ? null : onNext)["›"]
        ];

    // The 6x7 month grid (role=grid). `view` selects the month; `cursor` is the virtually-focused day
    // (aria-activedescendant target); `selected` is the bound value. Out-of-[min,max] and predicate-
    // disabled days are greyed and non-clickable. Always 42 cells so the popover height never jumps.
    internal static Component CalendarGrid(
        DateOnly view, DateOnly cursor, DateOnly? selected,
        DateOnly? min, DateOnly? max, Func<DateOnly, bool>? disable,
        CultureInfo culture, string cellIdPrefix, string gridId, Func<DateOnly, Task> onPick)
    {
        var firstOfMonth = new DateOnly(view.Year, view.Month, 1);
        var firstDow = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var lead = ((int)firstOfMonth.DayOfWeek - firstDow + 7) % 7;
        var start = firstOfMonth.AddDays(-lead);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var rows = new List<Component>(7) { WeekdayHeaderRow(culture) };
        for (var week = 0; week < 6; week++)
        {
            var cells = new List<Component>(7);
            for (var d = 0; d < 7; d++)
            {
                var cell = start.AddDays((week * 7) + d);
                var outOfRange = (min is { } mn && cell < mn) || (max is { } mx && cell > mx);
                var predicateOff = disable?.Invoke(cell) == true;
                var cellDisabled = outOfRange || predicateOff;
                var isSelected = selected is { } s && s == cell;
                var isCursor = cell == cursor;
                var otherMonth = cell.Month != view.Month;

                var aria = new Dictionary<string, string?> { ["label"] = cell.ToString("D", culture) };
                if (isSelected)
                {
                    aria["selected"] = "true";
                }

                if (cellDisabled)
                {
                    aria["disabled"] = "true";
                }

                cells.Add(Div(
                    Id: CellId(cellIdPrefix, cell),
                    Class: BsClass.Join("bs-cal-cell",
                        otherMonth ? "bs-cal-muted" : null,
                        cell == today ? "bs-cal-today" : null,
                        isSelected ? "active" : null,
                        isCursor ? "bs-cal-focus" : null,
                        cellDisabled ? "disabled" : null),
                    Role: "gridcell",
                    Aria: aria,
                    OnClickAsync: cellDisabled ? null : () => onPick(cell),
                    Key: (week * 7) + d)[cell.Day.ToString(culture)]);
            }

            rows.Add(Div(Role: "row", Key: week + 1)[cells]);
        }

        return Div(
            Class: "bs-cal",
            Id: gridId,
            Role: "grid",
            Aria: new Dictionary<string, string?> { ["label"] = view.ToString("y", culture) })[rows];
    }

    // Two scrollable listboxes (hours 0-23, minutes 0..<60 by step). The active cell reflects the current
    // value; clicking one composes a new time in the picker. Named columns carry aria labels for AT.
    internal static Component TimeColumns(
        TimeOnly? current, int minuteStep, CultureInfo culture,
        Func<int, Task> onHour, Func<int, Task> onMinute)
    {
        var step = minuteStep < 1 ? 1 : minuteStep;
        var hours = new List<Component>(24);
        for (var h = 0; h < 24; h++)
        {
            var hh = h;
            hours.Add(Button(Type: "button", Key: h,
                Class: BsClass.Join("dropdown-item", "bs-time-item", current?.Hour == h ? "active" : null),
                Aria: current?.Hour == h ? Selected : null,
                OnClickAsync: () => onHour(hh))[h.ToString("00", culture)]);
        }

        var minutes = new List<Component>();
        for (var m = 0; m < 60; m += step)
        {
            var mm = m;
            minutes.Add(Button(Type: "button", Key: m,
                Class: BsClass.Join("dropdown-item", "bs-time-item", current?.Minute == m ? "active" : null),
                Aria: current?.Minute == m ? Selected : null,
                OnClickAsync: () => onMinute(mm))[m.ToString("00", culture)]);
        }

        return Div(Class: BsClass.Join("bs-time", Display.Flex(), Flex.Gap(1)))[
            Div(Class: "bs-time-col", Role: "listbox",
                Aria: new Dictionary<string, string?> { ["label"] = "Hour" })[hours],
            Span(Class: "bs-time-sep")[":"],
            Div(Class: "bs-time-col", Role: "listbox",
                Aria: new Dictionary<string, string?> { ["label"] = "Minute" })[minutes]
        ];
    }

    private static readonly IReadOnlyDictionary<string, string?> Selected =
        new Dictionary<string, string?> { ["selected"] = "true" };
}
