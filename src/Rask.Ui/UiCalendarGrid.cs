using System.Globalization;

namespace Rask;

/// <summary>
/// The month grid every calendar in the kit draws — Flux UI's <c>flux:calendar</c>: the header with its month
/// steps, the weekday row, and a button per day.
/// </summary>
/// <remarks>
/// Shared by the three calendars and the date pickers, which differ only in what "chosen" means for a day
/// (<see cref="UiCalendarPicks" />). This is the page's FIRST paint of it: the header and the months are the
/// browser's from then on (<c>data-rask-opaque</c>, drawn by <c>rask-calendar.ts</c>, which pages, walks and
/// picks as Flux's script does), and the page hears the value through the calendar's one bound field.
/// </remarks>
internal abstract partial class UiCalendarGrid : global::Rask.Core.RaskMarkup
{
    private static readonly UiPartMarker Marker = new("ui-calendar");

    // The box the runtime reads the width from, and the parity tool's name for a node Flux has none of.
    private static readonly Dictionary<string, string?> NarrowMarks = new(StringComparer.Ordinal)
    {
        ["ui-calendar-narrow"] = "",
        ["rask-calendar-part"] = "narrow",
    };

    private static readonly Dictionary<string, string?> StaticMonths = new(StringComparer.Ordinal)
    {
        ["ui-calendar-months"] = "",
        ["rask-calendar-part"] = "months",
    };

    // The keys the runtime handles here do not scroll the page behind the grid (Space is not one of them, as on Flux).
    private static readonly Dictionary<string, string?> Months = new(StaticMonths, StringComparer.Ordinal)
    {
        ["rask-contain-keys"] = "Arrows Home End PageUp PageDown",
    };

    /// <summary>The first of the month a calendar shows first.</summary>
    internal static DateOnly ViewOf(UiCalendarOptions options, UiCalendarPicks picks)
    {
        var opened = options.ForceOpenTo ? options.OpenTo ?? picks.First : picks.First ?? options.OpenTo;
        var shown = opened ?? options.Today;
        return new DateOnly(shown.Year, shown.Month, 1);
    }

    /// <summary>
    ///     The calendar. <paramref name="aside" /> and <paramref name="footer" /> are a date picker's: its preset
    ///     list before the months, and its confirmation buttons after them; it draws the calendar unmarked, and
    ///     carries the runtime's <paramref name="hook" /> itself.
    /// </summary>
    internal static global::Rask.Core.Component Render(
        UiCalendarOptions options,
        UiCalendarPicks picks,
        string? rootClass = null,
        global::Rask.Core.Component? aside = null,
        global::Rask.Core.Component? footer = null,
        bool marked = true,
        (string Config, global::Rask.Core.Component Field)? hook = null)
    {
        var view = ViewOf(options, picks);
        var months = Enumerable.Range(0, options.Months).Select(view.AddMonths).ToList();
        var shown = months.SelectMany(month => Weeks(options, month)).SelectMany(week => week).ToHashSet();
        var stop = TabStop(options, picks, months, shown);
        var host = Host(options, months, stop);

        var calendar = Div
            .Id(options.Id)
            .Class(UiClass.Compose(rootClass ?? UiCalendarLook.Root, options.Class));
        if (marked)
        {
            // Inside a date picker the calendar is the picker's own part, and Flux leaves it unmarked there.
            calendar = calendar.Data(hook is { } h ? Marker.With(options.Data, "rask-calendar", h.Config) : Marker.With(options.Data));
        }

        return calendar[
            hook?.Field,
            aside,
            options.WithInputs is { } variant && !string.Equals(picks.Mode, "multiple", StringComparison.Ordinal) ? Inputs(options, picks, variant) : null,
            HostedElement.Tag("div").Opaque(true).Class(UiCalendarLook.HeaderHost).Attributes(UiCalendarHook.Named("header"))[
                Div.Class(UiCalendarLook.HeaderBar)[
                    Header.Class(UiCalendarLook.Header)[
                        Div.Class(UiCalendarLook.HeaderStart)[
                            options.SelectableHeader ? MonthSelect(options, view) : null,
                            options.SelectableHeader ? YearSelect(options, view) : null
                        ],
                        Div.Class(UiCalendarLook.HeaderEnd)[
                            options.WithToday ? TodayStep(options) : null,
                            options.Navigation ? Step(options, view, months, -1) : null,
                            options.Navigation ? Step(options, view, months, 1) : null
                        ]
                    ]
                ]
            ],
            HostedElement.Tag("div").Opaque(true).Class(UiCalendarLook.Months).Data(options.Static ? StaticMonths : Months)[
                months.Select((month, at) => Month(options, picks, month, month == host ? stop : null, at > 0))
            ],
            footer,
            options.Months > 1 || options.InPopup ? Narrow() : null
        ];
    }

    // Asked only where the answer changes something: several months, or a date picker's popup.
    private static HTMLSpanElement Narrow() =>
        Span.Class(UiCalendarLook.Narrow).Data(NarrowMarks).Aria("hidden", "true");

    // Two months side by side draw the days between them twice. The tab stop is one button: the day in its own
    // month where that month is shown, else the first month that draws it as a neighbour's.
    private static DateOnly Host(UiCalendarOptions options, List<DateOnly> months, DateOnly stop)
    {
        var own = months.FindIndex(month => month.Year == stop.Year && month.Month == stop.Month);
        var drawn = own >= 0 ? own : months.FindIndex(month => Weeks(options, month).Exists(week => week.Contains(stop)));
        return months[Math.Max(drawn, 0)];
    }

    // ---- inputs -----------------------------------------------------------------------------------

    // Flux's `with-inputs`: a field to type the day into, or a range's start and end, over the months. They are the
    // browser's once drawn (rask-calendar.ts reads what is typed and fills in what is picked), so they sit in a
    // host no render reaches into, and send nothing of their own.
    private static global::Rask.Core.Component Inputs(UiCalendarOptions options, UiCalendarPicks picks, Ui.DatePickerInputVariant variant) =>
        HostedElement.Tag("div").Opaque(true).Class(UiCalendarLook.Inputs).Attributes(UiCalendarHook.Named("inputs"))[
            picks.Typed.Select((day, at) => Div.Key(at).Class(UiCalendarLook.Input)[
                TypedDay(options, day, variant)
            ])
        ];

    private static global::Rask.Core.Component TypedDay(UiCalendarOptions options, DateOnly? day, Ui.DatePickerInputVariant variant)
    {
        var iso = day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        return variant == Ui.DatePickerInputVariant.Native
            ? Input.Value(iso).Type(InputType.Date).Class(UiDatePickerLook.Native).Data("rask-date", string.Empty)
                .Attributes(UiRuntimeMarks.NotFormData, ("dir", "ltr"))
            : Div.Class(UiDatePickerLook.Segments).Data("rask-segments", string.Empty).Attributes(("dir", "ltr"))[
                UiDateParts.Of(options.Culture, disabled: false),
                Input.Value(iso).Type(InputType.Hidden).Attributes(UiRuntimeMarks.NotFormData)
            ];
    }

    // ---- header -----------------------------------------------------------------------------------

    private static global::Rask.Core.Component Step(UiCalendarOptions options, DateOnly view, List<DateOnly> months, int by)
    {
        // Nothing to page to: the whole month that way lies outside Min or Max.
        var off = by < 0
            ? options.Min is { } min && view.AddDays(-1) < min
            : options.Max is { } max && months[^1].AddMonths(1) > max;

        var label = by < 0
            ? RaskStrings.Get(RaskString.PickerPreviousMonth, "Previous month")
            : RaskStrings.Get(RaskString.PickerNextMonth, "Next month");

        return Button
            .Key(by < 0 ? "previous" : "next")
            .Type(ButtonType.Button)
            .Class(UiCalendarLook.Step)
            .Disabled(off)
            .Aria(off ? [("label", label), ("disabled", "true")] : [("label", label)])
            .Attributes(UiCalendarHook.Named(by < 0 ? "previous" : "next"))[
            Ui.Icon.Name(by < 0 ? Ui.IconName.ChevronLeft : Ui.IconName.ChevronRight).Mini.Class(UiCalendarLook.StepIcon),
            Ui.Icon.Name(by < 0 ? Ui.IconName.ChevronRight : Ui.IconName.ChevronLeft).Mini.Class(UiCalendarLook.StepIconRtl)
        ];
    }

    // Flux's shortcut: from another month it comes back to this one; already there, it picks today.
    private static global::Rask.Core.Component TodayStep(UiCalendarOptions options) =>
        Button
            .Key("today")
            .Type(ButtonType.Button)
            .Class(UiCalendarLook.Step)
            .Aria("label", RaskStrings.Get(RaskString.CalendarToday, "Today"))
            .Attributes(UiCalendarHook.Named("today"))[
            Div.Class(UiCalendarLook.TodayBox)[
                Div.Class(UiCalendarLook.TodayNumber)[options.Today.Day.ToString(options.Culture)],
                Ui.Icon.Name(Ui.IconName.Calendar).Mini.Class(UiCalendarLook.TodayIcon)
            ]
        ];

    private static global::Rask.Core.Component MonthSelect(UiCalendarOptions options, DateOnly view) =>
        Div.Class(UiCalendarLook.SelectHost)[
            Select
                .Value(Invariant(view.Month))
                .Attributes(UiRuntimeMarks.NotFormData, UiCalendarHook.Named("month"))
                .Class(UiCalendarLook.Select)[
                Enumerable.Range(1, 12).Select(month =>
                    Option.Key(month).Value(Invariant(month))[options.Culture.DateTimeFormat.GetAbbreviatedMonthName(month)])
            ]
        ];

    // A century back and a decade on from today, as Flux lists them.
    private static global::Rask.Core.Component YearSelect(UiCalendarOptions options, DateOnly view) =>
        Div.Class(UiCalendarLook.SelectHost)[
            Select
                .Value(Invariant(view.Year))
                .Attributes(UiRuntimeMarks.NotFormData, UiCalendarHook.Named("year"))
                .Class(UiCalendarLook.Select)[
                Enumerable.Range(options.Today.Year - 100, 111).Select(year => Option.Key(year).Value(Invariant(year))[Invariant(year)])
            ]
        ];

    private static string Invariant(int number) => number.ToString(CultureInfo.InvariantCulture);

    // ---- one month --------------------------------------------------------------------------------

    private static global::Rask.Core.Component Month(UiCalendarOptions options, UiCalendarPicks picks, DateOnly month, DateOnly? stop, bool folded)
    {
        var cell = Cell(options);
        var names = options.Culture.DateTimeFormat;
        var grid = Div.Key(month.DayNumber).Role("grid").Data("month", "");

        return (folded ? grid.Class(UiCalendarLook.MonthFolded) : grid)[
            // The lists in the header name the first month; every month after it keeps its title, as without them.
            Div.Class(options.SelectableHeader && !folded ? UiCalendarLook.HeadingHidden : UiCalendarLook.Heading)[
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
                    Weeks(options, month) is var weeks ? weeks.Select((week, at) =>
                        Tr.Key(week[0].DayNumber).Class(at == 0 ? UiCalendarLook.Row : UiCalendarLook.RowBelow)[
                            options.WeekNumbers
                                ? Td.Key("week").Class(UiClass.Compose("p-0 relative", UiCalendarLook.WeekNumber, cell))[
                                    WeekOf(week).ToString(options.Culture)
                                ]
                                : null,
                            week.Select((day, slot) => Day(options, picks, month, day, slot, stop, at == weeks.Count - 1))
                        ]) : null
                ]
            ]
        ];
    }

    private static string Cell(UiCalendarOptions options) =>
        options.WeekNumbers ? UiCalendarLook.CellBesideWeek(options.Size) : UiCalendarLook.Cell(options.Size);

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
        UiCalendarOptions options, UiCalendarPicks picks, DateOnly month, DateOnly day, int slot, DateOnly? stop, bool lastRow)
    {
        var marks = picks.Marks(day);
        var outside = day.Month != month.Month;
        var unavailable = options.Unavailable?.Contains(day) == true;
        var blocked = Blocked(options, day);
        var today = day == options.Today;
        var cell = Cell(options);
        var name = day.ToString("D", options.Culture);

        var td = Td
            .Key(day.DayNumber)
            .Role("gridcell")
            .Class(UiClass.Compose(
                UiCalendarLook.Td(slot == 0 && !options.WeekNumbers, Last(slot, lastRow, marks), outside && !options.Static, unavailable, marks), cell))
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

        var button = Button.Type(ButtonType.Button).Disabled(blocked).TabIndex(day == stop ? 0 : -1)
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

    // A week's last day is rounded at its end — except, measured on Flux's, a day the range runs on past in the
    // month's last row: the range goes on into the next month.
    private static bool Last(int slot, bool lastRow, UiCalendarMarks marks) => slot == 6 && !(lastRow && marks.InRange);

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

    internal static bool Blocked(UiCalendarOptions options, DateOnly day) =>
        (options.Min is { } min && day < min)
        || (options.Max is { } max && day > max)
        || options.Unavailable?.Contains(day) == true;

    // Where Tab lands: the first chosen day, else today, else the 1st — as Flux roves.
    private static DateOnly TabStop(UiCalendarOptions options, UiCalendarPicks picks, List<DateOnly> months, HashSet<DateOnly> shown)
    {
        DateOnly?[] wanted = [picks.First, options.Today, months[0]];
        return wanted.FirstOrDefault(day => day is { } d && shown.Contains(d) && !Blocked(options, d)) ?? months[0];
    }
}
