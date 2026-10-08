namespace Rask.Site.Features.UiKit;

/// <summary>Flux UI's calendar, date picker and time picker, example by example.</summary>
public sealed partial class UiKitDataInputDemo
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);

    private DateOnly _date;
    private List<DateOnly> _daysOff = [];
    private UiDateRange _stay;
    private DateOnly _arrival;
    private UiDateRange _report;
    private readonly TimeOnly?[] _times = new TimeOnly?[9];
    private List<TimeOnly> _slots = [];

    private Component CalendarSection() =>
        Section(
            "Calendar — Flux UI's, example by example",
            "Flux's mode is the step that opens it: Ui.Calendar picks a day, Ui.Calendar.Multiple several, "
            + "Ui.Calendar.Range a range — its first click is held and drawn, the second writes the whole range. One day "
            + "is in the Tab order; the arrows walk days and weeks and take the focus with them, PageUp and PageDown page "
            + "a month, Enter picks.",
            Div.Data(Testid("ui-calendar")).Class("grid gap-8 lg:grid-cols-2")[
                Example("A day", "ui-calendar-state", _date == default ? "No date chosen." : $"Chosen: {Iso(_date)}",
                    Ui.Calendar.Value(_date).Key("day").OnChange(d => { _date = d; })),
                Example("Several days", "ui-calendar-days", $"{_daysOff.Count} days off",
                    Ui.Calendar.Multiple.Values(_daysOff).Key("days").OnChange(days => { _daysOff = [.. days]; })),
                Div.Key("range").Class("lg:col-span-2")[
                    Example("A range", "ui-calendar-stay",
                        _stay == default ? "No stay chosen." : $"Stay: {Iso(_stay.Start)} to {Iso(_stay.End)}",
                        Ui.Calendar.Range.Value(_stay).Key("stay").OnChange(r => { _stay = r; }))
                ],
                Example("Size", Ui.Calendar.Value(default(DateOnly)).Key("size").Xl),
                Example("Static", Ui.Calendar.Value(Today).Key("static").Static().Xs.Navigation(false)),
                Example("Nothing after today", Ui.Calendar.Value(default(DateOnly)).Key("max").Max(Today)),
                Example("Unavailable dates",
                    Ui.Calendar.Value(default(DateOnly)).Key("unavailable").Unavailable([Today.AddDays(-1), Today.AddDays(1)])),
                Example("With today shortcut", Ui.Calendar.Value(default(DateOnly)).Key("today").WithToday()),
                Example("Selectable header", Ui.Calendar.Value(default(DateOnly)).Key("header").SelectableHeader()),
                Example("Fixed weeks", Ui.Calendar.Value(default(DateOnly)).Key("fixed").FixedWeeks()),
                Example("Start day", Ui.Calendar.Value(default(DateOnly)).Key("start").StartDay(DayOfWeek.Monday)),
                Example("Open to", Ui.Calendar.Value(default(DateOnly)).Key("open").OpenTo(NextYear())),
                Example("Week numbers", Ui.Calendar.Value(default(DateOnly)).Key("weeks").WeekNumbers()),
                Example("Localization", Ui.Calendar.Value(default(DateOnly)).Key("locale").Locale("ja-JP"))
            ]);

    private Component DatePickerSection() =>
        Section(
            "Date picker — Flux UI's, example by example",
            "A field-shaped button over the calendar in a popover the browser opens and closes: a day closes it on "
            + "the pick, a range on its second click, a preset at once. Type(Input) swaps the button for month, day "
            + "and year fields typed as one: a part that is full moves on to the next.",
            Div.Data(Testid("ui-date-picker")).Class("grid max-w-3xl gap-6 sm:grid-cols-2")[
                Example("A day", "ui-date-picker-state", _arrival == default ? "No arrival chosen." : $"Arrival: {Iso(_arrival)}",
                    Ui.DatePicker.Value(_arrival).Key("day").Label("Arrival").OnChange(d => { _arrival = d; })),
                Example("Input trigger", Ui.DatePicker.Value(_arrival).Key("input").Type(Ui.DatePickerType.Input)
                    .OnChange(d => { _arrival = d; })),
                Example("Range picker", "ui-date-picker-range",
                    _report == default ? "No range chosen." : $"Range: {Iso(_report.Start)} to {Iso(_report.End)}",
                    Ui.DatePicker.Range.Value(_report).Key("range").OnChange(r => { _report = r; })),
                Example("At least three days", Ui.DatePicker.Range.Value(default(UiDateRange)).Key("min-range").MinRange(3)),
                Example("At most ten days", Ui.DatePicker.Range.Value(default(UiDateRange)).Key("max-range").MaxRange(10)),
                Example("Range with inputs", Ui.DatePicker.Range.Value(_report).Key("inputs").OnChange(r => { _report = r; })
                    .Trigger(Div.Class("flex flex-col gap-6 sm:flex-row sm:gap-4")[
                        Ui.DatePickerInput.Key("start").Label("Start"),
                        Ui.DatePickerInput.Key("end").Label("End")
                    ])),
                Example("Presets", Ui.DatePicker.Range.Value(_report).Key("presets").WithPresets().Min(new DateOnly(2012, 1, 1))
                    .OnChange(r => { _report = r; })),
                Example("Unavailable dates",
                    Ui.DatePicker.Value(default(DateOnly)).Key("unavailable").Unavailable([Today.AddDays(-1), Today.AddDays(1)])),
                Example("With today shortcut", Ui.DatePicker.Value(default(DateOnly)).Key("today").WithToday()),
                Example("Selectable header", Ui.DatePicker.Value(default(DateOnly)).Key("header").SelectableHeader()),
                Example("Fixed weeks", Ui.DatePicker.Value(default(DateOnly)).Key("fixed").FixedWeeks()),
                Example("Start day", Ui.DatePicker.Value(default(DateOnly)).Key("start").StartDay(DayOfWeek.Monday)),
                Example("Open to", Ui.DatePicker.Value(default(DateOnly)).Key("open").OpenTo(NextYear())),
                Example("Week numbers", Ui.DatePicker.Value(default(DateOnly)).Key("weeks").WeekNumbers()),
                Example("Localization", Ui.DatePicker.Value(default(DateOnly)).Key("locale").Locale("ja-JP"))
            ]);

    private Component TimePickerSection() =>
        Section(
            "Time picker — Flux UI's, example by example",
            "A button over a list of times. Bind a TimeOnly? for one time or none, a collection of TimeOnly for "
            + "several — that list stays open. The button keeps the focus: the arrows move through the list, Enter picks.",
            Div.Data(Testid("ui-time-picker")).Class("grid max-w-3xl gap-6 sm:grid-cols-2")[
                Example("A time", "ui-time-picker-state",
                    _times[0] is { } at ? $"Chosen: {at.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)}" : "No time chosen.",
                    Time(0, "time")),
                Example("Input trigger", Time(1, "input").Type(Ui.TimePickerType.Input)),
                Example("Without dropdown", Time(2, "typed").Type(Ui.TimePickerType.Input).Dropdown(false)),
                Example("Multiple times", "ui-time-picker-slots", $"{_slots.Count} times",
                    Ui.TimePicker.Value(_slots).Key("slots").Id("demo-time-slots").OnChange(slots => { _slots = [.. slots]; })),
                Example("12-hour", Time(3, "twelve").TwelveHour),
                Example("24-hour", Time(4, "twenty-four").TwentyFourHour),
                Example("Interval", Time(5, "interval").Interval(60)),
                Example("Min/max times", Time(6, "min-max").Min(new TimeOnly(9, 0)).Max(new TimeOnly(17, 0))),
                Example("Unavailable times", Time(7, "unavailable").Unavailable([
                    new TimeOnly(3, 0), new TimeOnly(4, 0), new UiTimeRange(new TimeOnly(5, 30), new TimeOnly(7, 29))
                ])),
                Example("Open to", Time(8, "open").OpenTo(new TimeOnly(10, 0))),
                Example("Localization", Ui.TimePicker.Of<TimeOnly?>().Key("locale").Id("demo-time-locale").Locale("ja-JP"))
            ]);

    // Each picker keeps its own time: an unbound one would forget the pick on the next render.
    private UiTimePicker<TimeOnly?> Time(int slot, string key) =>
        Ui.TimePicker.Value(_times[slot]).Key(key).Id("demo-time-" + key).OnChange(time => { _times[slot] = time; });

    // Flux's open-to example: the first of next month, a year on.
    private static DateOnly NextYear() => new DateOnly(Today.Year, Today.Month, 1).AddMonths(1).AddYears(1);

    private static Component Example(string title, Component control) =>
        Div.Key(title).Class("space-y-2")[
            P.Class("text-sm font-medium text-zinc-800 dark:text-white")[title],
            control
        ];

    private static Component Example(string title, string testid, string state, Component control) =>
        Div.Key(title).Class("space-y-2")[
            P.Class("text-sm font-medium text-zinc-800 dark:text-white")[title],
            control,
            P.Class("text-sm text-ui-muted").Data(Testid(testid))[state]
        ];

    private static string Iso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
