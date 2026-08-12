using System.Globalization;

namespace Rask.Bootstrap;

// Stateless render + date-math helpers shared by BsDatePicker / BsTimePicker / BsDateTimePicker so the
// month grid, time columns, header and week math live in exactly one place (the pickers hold the state
// and pass it in). Everything is culture-driven: weekday order, names and the month label come from
// CultureInfo.DateTimeFormat, while the bound value round-trips invariant ISO in the picker itself.
[global::Rask.Core.RaskMarkup]
internal static partial class PickerParts
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
            cells.Add(Div
                .Class(BsClass.Join("bs-cal-head", Txt.Muted))
                .Role("columnheader")
                .Aria(new Dictionary<string, string?> { ["label"] = dtf.DayNames[(int)dow] })
                .Key(i)[dtf.AbbreviatedDayNames[(int)dow]]);
        }

        return Div.Role("row").Key(0)[cells];
    }

    // The prev/next month header with the localized "MMMM yyyy" label. The nav aria-labels come from
    // `labels` (they have no CultureInfo source, unlike the month/day names).
    internal static Component MonthHeader(
        DateOnly view, CultureInfo culture, Action onPrev, Action onNext,
        bool prevDisabled, bool nextDisabled, BsPickerLabels labels) =>
        Div
            .Class(BsClass.Join(Display.Flex(), Flex.Align(BsAlign.Center),
            Flex.Justify(BsJustify.Between), Margin.Bottom(2)))[
            Button
                .Type("button")
                .Class("btn btn-sm btn-outline-secondary")
                .Disabled(prevDisabled ? true : null)
                .Aria(new Dictionary<string, string?> { ["label"] = labels.PreviousMonth })
                .OnClick(prevDisabled ? null : onPrev)["‹"],
            Span.Class(Font.Semibold)[view.ToString("y", culture)],
            Button
                .Type("button")
                .Class("btn btn-sm btn-outline-secondary")
                .Disabled(nextDisabled ? true : null)
                .Aria(new Dictionary<string, string?> { ["label"] = labels.NextMonth })
                .OnClick(nextDisabled ? null : onNext)["›"]
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

                cells.Add(Div
                    .Id(CellId(cellIdPrefix, cell))
                    .Class(BsClass.Join("bs-cal-cell",
                        otherMonth ? "bs-cal-muted" : null,
                        cell == today ? "bs-cal-today" : null,
                        isSelected ? "active" : null,
                        isCursor ? "bs-cal-focus" : null,
                        cellDisabled ? "disabled" : null))
                    .Role("gridcell")
                    .Aria(aria)
                    .OnClickAsync(cellDisabled ? null : () => onPick(cell))
                    .Key((week * 7) + d)[cell.Day.ToString(culture)]);
            }

            rows.Add(Div.Role("row").Key(week + 1)[cells]);
        }

        return Div
            .Class("bs-cal")
            .Id(gridId)
            .Role("grid")
            .Aria(new Dictionary<string, string?> { ["label"] = view.ToString("y", culture) })[rows];
    }

    // Scrollable listboxes (hours 0-23, minutes 0..<60 by step, and — when `seconds` — seconds 0..<60 by
    // step). The active cell reflects the current value; clicking one composes a new time in the picker.
    // Items outside [min,max] for their column (boundary-aware against the current hour/minute) are greyed
    // and non-clickable — the picker's own Clamp is the correctness guarantee regardless. Named columns
    // carry aria labels for AT, sourced from `labels`.
    internal static Component TimeColumns(
        TimeOnly? current, int minuteStep, bool seconds, int secondStep,
        TimeOnly? min, TimeOnly? max, CultureInfo culture, BsPickerLabels labels,
        Func<int, Task> onHour, Func<int, Task> onMinute, Func<int, Task>? onSecond)
    {
        var mStep = minuteStep < 1 ? 1 : minuteStep;
        var sStep = secondStep < 1 ? 1 : secondStep;
        var refT = current ?? new TimeOnly(0, 0);
        // The largest minute actually rendered in a column, so the boundary hour is greyed when even its last
        // stepped minute is still below Min (e.g. Min 10:59 with step 5 renders only …:55 — hour 10 is dead).
        var lastMinute = 59 / mStep * mStep;

        var hours = new List<Component>(24);
        for (var h = 0; h < 24; h++)
        {
            var hh = h;
            var off = (min is { } mn && (h < mn.Hour || (h == mn.Hour && lastMinute < mn.Minute)))
                      || (max is { } mx && h > mx.Hour);
            hours.Add(TimeItem(h, current?.Hour == h, off, culture, () => onHour(hh)));
        }

        var minutes = new List<Component>();
        for (var m = 0; m < 60; m += mStep)
        {
            var mm = m;
            var off = (min is { } mn && refT.Hour == mn.Hour && m < mn.Minute) ||
                      (max is { } mx && refT.Hour == mx.Hour && m > mx.Minute);
            minutes.Add(TimeItem(m, current?.Minute == m, off, culture, () => onMinute(mm)));
        }

        var cols = new List<Component>
        {
            Div
                .Class("bs-time-col")
                .Role("listbox")
                .Aria(new Dictionary<string, string?> { ["label"] = labels.Hour })[hours],
            Span.Class("bs-time-sep")[":"],
            Div
                .Class("bs-time-col")
                .Role("listbox")
                .Aria(new Dictionary<string, string?> { ["label"] = labels.Minute })[minutes],
        };

        if (seconds && onSecond is not null)
        {
            var secs = new List<Component>();
            for (var s = 0; s < 60; s += sStep)
            {
                var ss = s;
                var off = (min is { } mn && refT.Hour == mn.Hour && refT.Minute == mn.Minute && s < mn.Second) ||
                          (max is { } mx && refT.Hour == mx.Hour && refT.Minute == mx.Minute && s > mx.Second);
                secs.Add(TimeItem(s, current?.Second == s, off, culture, () => onSecond(ss)));
            }

            cols.Add(Span.Class("bs-time-sep")[":"]);
            cols.Add(Div
                .Class("bs-time-col")
                .Role("listbox")
                .Aria(new Dictionary<string, string?> { ["label"] = labels.Second })[secs]);
        }

        return Div.Class(BsClass.Join("bs-time", Display.Flex(), Flex.Gap(1)))[cols];
    }

    // One hour/minute/second option: 00-formatted, active when it matches the value, greyed and
    // non-clickable when out of range for its column. Reuses shared aria dictionaries so a full time
    // column (24 hours + minutes + seconds) doesn't allocate a dictionary per option on every render.
    private static Component TimeItem(
        int value, bool active, bool disabled, CultureInfo culture, Func<Task> onPick)
    {
        var aria = (active, disabled) switch
        {
            (true, true) => SelectedAndDisabled,
            (true, false) => Selected,
            (false, true) => DisabledAria,
            _ => null,
        };

        return Button
            .Type("button")
            .Key(value)
            .Class(BsClass.Join("dropdown-item", "bs-time-item", active ? "active" : null,
                disabled ? "disabled" : null))
            .Aria(aria)
            .OnClickAsync(disabled ? null : onPick)[value.ToString("00", culture)];
    }

    private static readonly IReadOnlyDictionary<string, string?> Selected =
        new Dictionary<string, string?> { ["selected"] = "true" };

    private static readonly IReadOnlyDictionary<string, string?> DisabledAria =
        new Dictionary<string, string?> { ["disabled"] = "true" };

    private static readonly IReadOnlyDictionary<string, string?> SelectedAndDisabled =
        new Dictionary<string, string?> { ["selected"] = "true", ["disabled"] = "true" };
}
