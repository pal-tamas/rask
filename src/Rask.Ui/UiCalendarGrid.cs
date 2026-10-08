using System.Globalization;

namespace Rask;

/// <summary>
/// The month grid every calendar in the kit draws — Flux UI's <c>flux:calendar</c>: the header with its month
/// steps, the weekday row, and a button per day.
/// </summary>
/// <remarks>
/// Shared by the three calendars and the date pickers, which differ only in what "chosen" means for a day
/// (<see cref="UiCalendarPicks" />). A markup host rather than a component: the state belongs to the control
/// that calls it (<see cref="UiCalendarState" />), and so do the handlers written here, which re-render it.
/// </remarks>
internal abstract partial class UiCalendarGrid : global::Rask.Core.RaskMarkup
{
    private static readonly UiPartMarker Marker = new("ui-calendar");

    private static readonly Dictionary<string, string?> StaticMonths = new(StringComparer.Ordinal) { ["ui-calendar-months"] = "" };

    // The runtime's two hooks for a grid of days: the keys handled here do not scroll the page behind it (Space
    // is not one of them, as on Flux), and the browser's focus goes with the day that is the tab stop.
    private static readonly Dictionary<string, string?> Months = new(StaticMonths, StringComparer.Ordinal)
    {
        ["rask-contain-keys"] = "Arrows Home End PageUp PageDown",
        ["rask-focus-follows"] = "",
    };

    /// <summary>The first of the month a calendar shows first.</summary>
    internal static DateOnly ViewOf(UiCalendarOptions options, UiCalendarState state, UiCalendarPicks picks)
    {
        var opened = options.ForceOpenTo ? options.OpenTo ?? picks.First : picks.First ?? options.OpenTo;
        var shown = state.View ?? opened ?? options.Today;
        return new DateOnly(shown.Year, shown.Month, 1);
    }

    /// <summary>
    ///     The calendar. <paramref name="aside" /> and <paramref name="footer" /> are a date picker's: its preset
    ///     list before the months, and its confirmation buttons after them; it draws the calendar unmarked.
    /// </summary>
    internal static global::Rask.Core.Component Render(
        UiCalendarOptions options,
        UiCalendarState state,
        UiCalendarPicks picks,
        string? rootClass = null,
        global::Rask.Core.Component? aside = null,
        global::Rask.Core.Component? footer = null,
        bool marked = true)
    {
        var view = ViewOf(options, state, picks);
        var months = Enumerable.Range(0, options.Months).Select(view.AddMonths).ToList();
        var shown = months.SelectMany(month => Weeks(options, month)).SelectMany(week => week).ToHashSet();
        var stop = TabStop(options, state, picks, months, shown);
        // A paging key lets go of the focus for the one render it causes: no day asks for it, and it falls to
        // the page, as it does on Flux.
        var follows = !state.Dropped;
        (state.Unnamed, state.Dropped) = (state.Dropped, false);
        var host = Host(options, months, stop);

        var calendar = Div
            .Id(options.Id)
            .Class(UiClass.Compose(rootClass ?? UiCalendarLook.Root, options.Class));
        if (marked)
        {
            // Inside a date picker the calendar is the picker's own part, and Flux leaves it unmarked there.
            calendar = calendar.Data(Marker.With(options.Data));
        }


        var grids = Div.Class(UiCalendarLook.Months).Data(options.Static ? StaticMonths : Months);
        if (!options.Static)
        {
            grids = grids.OnKeyDown(e => KeyAsync(e.Key, options, state, picks, months, shown, stop));
        }

        return calendar[
            aside,
            Div.Class(UiCalendarLook.HeaderHost)[
                Div.Class(UiCalendarLook.HeaderBar)[
                    Header.Class(UiCalendarLook.Header)[
                        Div.Class(UiCalendarLook.HeaderStart)[
                            options.SelectableHeader ? MonthSelect(options, state, view) : null,
                            options.SelectableHeader ? YearSelect(options, state, view) : null
                        ],
                        Div.Class(UiCalendarLook.HeaderEnd)[
                            options.WithToday ? TodayStep(options, state, picks, months) : null,
                            options.Navigation ? Step(options, state, view, months, -1) : null,
                            options.Navigation ? Step(options, state, view, months, 1) : null
                        ]
                    ]
                ]
            ],
            grids[months.Select(month => Month(options, state, picks, month, month == host ? stop : null, follows))],
            footer
        ];
    }

    // Two months side by side draw the days between them twice. The tab stop is one button: the day in its own
    // month where that month is shown, else the first month that draws it as a neighbour's.
    private static DateOnly Host(UiCalendarOptions options, List<DateOnly> months, DateOnly? stop)
    {
        var own = months.FindIndex(month => month.Year == stop?.Year && month.Month == stop?.Month);
        var drawn = own >= 0 ? own : months.FindIndex(month => Weeks(options, month).Exists(week => week.Contains(stop ?? default)));
        return months[Math.Max(drawn, 0)];
    }

    // ---- header -----------------------------------------------------------------------------------

    private static global::Rask.Core.Component Step(
        UiCalendarOptions options, UiCalendarState state, DateOnly view, List<DateOnly> months, int by)
    {
        // Nothing to page to: the whole month that way lies outside Min or Max.
        var off = by < 0
            ? options.Min is { } min && view.AddDays(-1) < min
            : options.Max is { } max && months[^1].AddMonths(1) > max;

        var label = by < 0 ? "Previous month" : "Next month";

        return Button
            .Key(by < 0 ? "previous" : "next")
            .Type(ButtonType.Button)
            .Class(UiCalendarLook.Step)
            .Disabled(off)
            .Aria(off ? [("label", label), ("disabled", "true")] : [("label", label)])
            .OnClick(() => Page(state, view.AddMonths(by)))[
            Ui.Icon.Name(by < 0 ? Ui.IconName.ChevronLeft : Ui.IconName.ChevronRight).Mini.Class(UiCalendarLook.StepIcon),
            Ui.Icon.Name(by < 0 ? Ui.IconName.ChevronRight : Ui.IconName.ChevronLeft).Mini.Class(UiCalendarLook.StepIconRtl)
        ];
    }

    private static void Page(UiCalendarState state, DateOnly view)
    {
        (state.View, state.Cursor) = (view, null);
    }

    // Flux's shortcut: from another month it comes back to this one; already there, it picks today.
    private static global::Rask.Core.Component TodayStep(
        UiCalendarOptions options, UiCalendarState state, UiCalendarPicks picks, List<DateOnly> months)
    {
        var today = options.Today;
        var here = months.Exists(month => month.Year == today.Year && month.Month == today.Month);

        return Button
            .Key("today")
            .Type(ButtonType.Button)
            .Class(UiCalendarLook.Step)
            .Aria("label", "Today")
            .OnClick(async () =>
            {
                if (here)
                {
                    await PickAsync(options, state, picks, today).ConfigureAwait(false);
                }
                else
                {
                    Page(state, new DateOnly(today.Year, today.Month, 1));
                }
            })[
            Div.Class(UiCalendarLook.TodayBox)[
                Div.Class(UiCalendarLook.TodayNumber)[today.Day.ToString(options.Culture)],
                Ui.Icon.Name(Ui.IconName.Calendar).Mini.Class(UiCalendarLook.TodayIcon)
            ]
        ];
    }

    private static global::Rask.Core.Component MonthSelect(UiCalendarOptions options, UiCalendarState state, DateOnly view) =>
        Div.Class(UiCalendarLook.SelectHost)[
            Select
                .Value(Invariant(view.Month))
                .OnChange(month => Page(state, new DateOnly(view.Year, int.Parse(month, CultureInfo.InvariantCulture), 1)))
                .Class(UiCalendarLook.Select)[
                Enumerable.Range(1, 12).Select(month =>
                    Option.Key(month).Value(Invariant(month))[options.Culture.DateTimeFormat.GetAbbreviatedMonthName(month)])
            ]
        ];

    // A century back and a decade on from today, as Flux lists them.
    private static global::Rask.Core.Component YearSelect(UiCalendarOptions options, UiCalendarState state, DateOnly view) =>
        Div.Class(UiCalendarLook.SelectHost)[
            Select
                .Value(Invariant(view.Year))
                .OnChange(year => Page(state, new DateOnly(int.Parse(year, CultureInfo.InvariantCulture), view.Month, 1)))
                .Class(UiCalendarLook.Select)[
                Enumerable.Range(options.Today.Year - 100, 111).Select(year => Option.Key(year).Value(Invariant(year))[Invariant(year)])
            ]
        ];

    private static string Invariant(int number) => number.ToString(CultureInfo.InvariantCulture);

    // ---- one month --------------------------------------------------------------------------------

    private static global::Rask.Core.Component Month(
        UiCalendarOptions options, UiCalendarState state, UiCalendarPicks picks, DateOnly month, DateOnly? stop, bool follows)
    {
        var cell = UiCalendarLook.Cell(options.Size);
        var names = options.Culture.DateTimeFormat;

        return Div.Key(month.DayNumber).Role("grid").Data("month", "")[
            Div.Class(options.SelectableHeader ? UiCalendarLook.HeadingHidden : UiCalendarLook.Heading)[
                Div.Class(UiCalendarLook.HeadingText)[month.ToString("Y", options.Culture)]
            ],
            Table[
                Thead[
                    Tr.Class(UiCalendarLook.Row)[
                        options.WeekNumbers ? Weekday(cell, "#", "#") : null,
                        Enumerable.Range(0, 7).Select(i =>
                        {
                            var day = (DayOfWeek)(((int)options.StartDay + i) % 7);
                            return Weekday(cell, day, names.GetShortestDayName(day));
                        })
                    ]
                ],
                Tbody[
                    Weeks(options, month).Select((week, at) =>
                        Tr.Key(week[0].DayNumber).Class(at == 0 ? UiCalendarLook.Row : UiCalendarLook.RowBelow)[
                            options.WeekNumbers
                                ? Td.Key("week").Class(UiClass.Compose("p-0 relative", UiCalendarLook.WeekNumber, cell))[
                                    WeekOf(week).ToString(options.Culture)
                                ]
                                : null,
                            week.Select((day, slot) => Day(options, state, picks, month, day, slot, stop, follows))
                        ])
                ]
            ]
        ];
    }

    private static global::Rask.Core.Component Weekday(string cell, object key, string name) =>
        Th.Key(key).Class(UiClass.Compose("p-0", UiCalendarLook.Weekday, cell)).Attributes(("scope", "col"))[
            Div.Class(UiCalendarLook.WeekdayText)[name]
        ];

    // The ISO week its Thursday falls in, which is the week a row is known by whichever day it starts on.
    private static int WeekOf(DateOnly[] week) =>
        ISOWeek.GetWeekOfYear(week.First(day => day.DayOfWeek == DayOfWeek.Thursday).ToDateTime(TimeOnly.MinValue));

    /// <summary>The rows a month is drawn as, the neighbouring months' days filling the first and last.</summary>
    internal static List<DateOnly[]> Weeks(UiCalendarOptions options, DateOnly month)
    {
        // How many days of the month before lead in. The +7 keeps it non-negative whichever day the week starts on.
        var lead = ((int)month.DayOfWeek - (int)options.StartDay + 7) % 7;
        var rows = options.FixedWeeks ? 6 : (lead + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7;
        var first = month.AddDays(-lead);

        return [.. Enumerable.Range(0, rows).Select(row => Enumerable.Range(0, 7).Select(slot => first.AddDays((row * 7) + slot)).ToArray())];
    }

    // ---- one day ----------------------------------------------------------------------------------

    private static global::Rask.Core.Component Day(
        UiCalendarOptions options,
        UiCalendarState state,
        UiCalendarPicks picks,
        DateOnly month,
        DateOnly day,
        int slot,
        DateOnly? stop,
        bool follows)
    {
        var marks = picks.Marks(day);
        var outside = day.Month != month.Month;
        var unavailable = options.Unavailable?.Contains(day) == true;
        var blocked = Blocked(options, picks, day);
        var today = day == options.Today;
        var cell = UiCalendarLook.Cell(options.Size);
        var name = day.ToString("D", options.Culture);

        var td = Td
            .Key(day.DayNumber)
            .Role("gridcell")
            .Class(UiClass.Compose(
                UiCalendarLook.Td(slot == 0 && !options.WeekNumbers, slot == 6, outside && !options.Static, unavailable, marks), cell))
            .Data(States(day, outside, today, unavailable, marks))
            .Aria(CellAria(options, picks, marks, name, blocked));

        var number = day.Day.ToString(options.Culture);
        if (options.Static)
        {
            return td[
                Div.Class(UiClass.Compose(marks.Selected ? UiCalendarLook.DayStaticSelected : UiCalendarLook.DayStatic, cell))[
                    Div.Class(today ? UiCalendarLook.StaticDot : UiCalendarLook.StaticDotHidden)[
                        Div.Class(UiClass.Compose("mb-1", marks.Selected ? UiCalendarLook.DotSelected : UiCalendarLook.Dot))
                    ],
                    number
                ]
            ];
        }

        var dot = Div.Class(today ? UiCalendarLook.TodayDot : UiCalendarLook.TodayDotHidden)[
            Div.Class(marks.Selected ? UiCalendarLook.DotSelected : UiCalendarLook.Dot)
        ];

        var button = DayButton(options, state, picks, day, blocked, day == stop, follows)
            .Class(UiClass.Compose(marks.Selected ? UiCalendarLook.DaySelected : UiCalendarLook.Day, cell))
            .Aria("label", name);

        return td[
            Div.Class(UiCalendarLook.TooltipHost)[
                button[
                    Div.Class(UiCalendarLook.DayText)[dot, Div[number]]
                ]
            ]
        ];
    }

    // Only what is so: a null value would be written as a bare attribute.
    private static Dictionary<string, string?> CellAria(
        UiCalendarOptions options, UiCalendarPicks picks, UiCalendarMarks marks, string name, bool blocked)
    {
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (options.Static)
        {
            aria["label"] = name;
        }
        else if (blocked)
        {
            aria["disabled"] = "true";
        }

        if (Selected(marks, picks) is { } selected)
        {
            aria["selected"] = selected;
        }

        return aria;
    }

    // A range leaves `aria-selected` off a day until the day is in it; a day or several say "false".
    private static string? Selected(UiCalendarMarks marks, UiCalendarPicks picks)
    {
        if (marks.Selected || marks.InRange)
        {
            return "true";
        }

        return picks.Range ? null : "false";
    }

    private static HTMLButtonElement DayButton(
        UiCalendarOptions options, UiCalendarState state, UiCalendarPicks picks, DateOnly day, bool blocked, bool stop, bool follows)
    {
        // The browser closes the picker on the same click that finishes the choice — no runtime, and the C#
        // handler still runs.
        var closes = !blocked && picks.Finishes(day) ? options.Closes : null;
        List<(string, string?)> extra = [];
        if (closes is not null)
        {
            extra.AddRange([("popovertarget", closes), ("popovertargetaction", "hide")]);
        }

        var button = Button.Type(ButtonType.Button).Disabled(blocked).TabIndex(stop ? 0 : -1);
        if (extra.Count > 0)
        {
            button = button.Attributes([.. extra]);
        }

        if (stop && follows)
        {
            button = button.Data("rask-focus-target", "");
        }

        if (blocked)
        {
            return button;
        }

        button = button.OnClick(() => PickAsync(options, state, picks, day));

        // A waiting range draws the stretch to the day under the pointer.
        return state.Anchor is null ? button : button.OnMouseEnter(() => { state.Hover = day; });
    }

    private static Dictionary<string, string?> States(DateOnly day, bool outside, bool today, bool unavailable, UiCalendarMarks marks)
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["date"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        foreach (var (name, on) in new[]
                 {
                     ("outside", outside), ("today", today), ("unavailable", unavailable), ("selected", marks.Selected),
                     ("start", marks.Start), ("end", marks.End), ("in-range", marks.InRange), ("end-preview", marks.EndPreview),
                 })
        {
            if (on)
            {
                data[name] = "";
            }
        }

        return data;
    }

    internal static bool Blocked(UiCalendarOptions options, UiCalendarPicks picks, DateOnly day) =>
        (options.Min is { } min && day < min)
        || (options.Max is { } max && day > max)
        || options.Unavailable?.Contains(day) == true
        || picks.Blocks(day);

    // ---- picking and the keyboard -----------------------------------------------------------------

    private static async Task PickAsync(UiCalendarOptions options, UiCalendarState state, UiCalendarPicks picks, DateOnly day)
    {
        if (Blocked(options, picks, day))
        {
            return;
        }

        state.Cursor = day;
        await picks.PickAsync(day).ConfigureAwait(false);
    }

    // Where Tab lands: the keyboard's day, else the first chosen one, else today, else the 1st — as Flux roves.
    private static DateOnly? TabStop(
        UiCalendarOptions options, UiCalendarState state, UiCalendarPicks picks, List<DateOnly> months, HashSet<DateOnly> shown)
    {
        DateOnly?[] wanted = [state.Cursor, picks.First, options.Today, months[0]];
        return wanted.FirstOrDefault(day => day is { } d && shown.Contains(d) && !Blocked(options, picks, d)) ?? months[0];
    }

    private static Task KeyAsync(
        string key,
        UiCalendarOptions options,
        UiCalendarState state,
        UiCalendarPicks picks,
        List<DateOnly> months,
        HashSet<DateOnly> shown,
        DateOnly? stop)
    {
        switch (key)
        {
            case Keys.ArrowLeft: Move(-1); break;
            case Keys.ArrowRight: Move(1); break;
            case Keys.ArrowUp: Move(-7); break;
            case Keys.ArrowDown: Move(7); break;
            // Flux pages a month on all four, with or without Shift, and lets go of the focus.
            case Keys.Home or Keys.PageUp: Drop(-1); break;
            case Keys.End or Keys.PageDown: Drop(1); break;
            default: break;
        }

        return Task.CompletedTask;

        void Drop(int by)
        {
            Page(state, months[0].AddMonths(by));
            state.Dropped = true;
        }

        void Move(int by)
        {
            if (stop is not { } from || Next(options, picks, from, by) is not { } to)
            {
                return;
            }

            (state.Cursor, state.Hover) = (to, state.Anchor is null ? null : to);
            if (!shown.Contains(to))
            {
                // Out of the drawn weeks: the view follows the day, by as little as shows it.
                state.View = to < months[0] ? new DateOnly(to.Year, to.Month, 1) : new DateOnly(to.Year, to.Month, 1).AddMonths(1 - months.Count);
            }
        }
    }

    // The next day that way which can be picked; none when Min or Max ends the walk first.
    private static DateOnly? Next(UiCalendarOptions options, UiCalendarPicks picks, DateOnly from, int by)
    {
        for (var (day, tries) = (from.AddDays(by), 0); tries < 62; day = day.AddDays(by), tries++)
        {
            if ((options.Min is { } min && day < min) || (options.Max is { } max && day > max))
            {
                return null;
            }

            if (!Blocked(options, picks, day))
            {
                return day;
            }
        }

        return null;
    }
}
