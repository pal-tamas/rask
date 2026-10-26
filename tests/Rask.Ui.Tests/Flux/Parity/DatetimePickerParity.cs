using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/datetime-picker</c>, example by example, as the page loads: every popup closed.</summary>
public sealed partial class DatetimePickerParity : DatetimePickerExamples
{
    public override string Page => "datetime-picker";

    protected override string Extra => string.Empty;
}

/// <summary>
///     The same pickers, each measured with its popup open — <c>scripts/flux/parity-date.mjs datetime-picker</c>, which
///     presses whatever carries <c>data-parity-open</c>: the picker, whose button (or custom trigger) fills it.
/// </summary>
public sealed partial class DatetimePickerOpenParity : DatetimePickerExamples
{
    // Flux's page goes on below its last example and above its first, so its popups open downwards and the pointer
    // that opened one stays in the middle of the screen.
    private const string Room = "<style>body{padding:900px 0}</style>";

    // The press opens the popup with no script. What the runtime does as it opens (rask-calendar.ts) — bring the
    // column's start row two rows down from its top — this static page does here, as the runtime would.
    private const string Mark =
        "<script>for (const picker of document.currentScript.parentElement.querySelectorAll('[data-ui-datetime-picker]')) {"
        + "picker.setAttribute('data-parity-open', '');"
        + "picker.querySelector('dialog').addEventListener('toggle', e => {"
        // The page behind an open popup takes no pointer (data-rask-lock).
        + "document.documentElement.style.pointerEvents = e.newState === 'open' ? 'none' : '';"
        + "const list = picker.querySelector('[data-rask-calendar-part=times]'), row = list?.querySelector('[data-rask-time-start]');"
        + "if (row) list.scrollTop = Math.max(0, row.offsetTop - 2 * row.offsetHeight); }); }</script>";

    public override string Page => "datetime-picker-open";

    protected override string Extra => Room + Mark;
}

/// <summary>The eighteen examples on Flux's datetime picker page, in its order and under its section ids.</summary>
/// <remarks>
///     Every picker is told the day and the time the measuring browser tells Flux's (<see cref="FluxClock" />) and the
///     locale that browser has; the moments an example is given are Flux's server's, which is on the real date.
/// </remarks>
public abstract partial class DatetimePickerExamples : FluxParity
{
    /// <summary>
    ///     The docs site's monospace face, which typed times are set in and sized by — a licensed font, given here as a
    ///     local face scaled to its advance — and the classes Flux's last example hands its two pickers.
    /// </summary>
    private const string App =
        "<style>@font-face{font-family:MonoLisa;src:local('Menlo'),local('Menlo Regular'),local('Menlo-Regular'),local('DejaVu Sans Mono'),local('DejaVuSansMono');size-adjust:106.3%}"
        + ":root{--font-mono:MonoLisa,monospace}.parity-flex-1{flex:1 1 0%}.parity-w-40{width:160px}</style>";

    /// <summary>What the page measured open adds to each example: room around it, and the mark the measurer presses.</summary>
    protected abstract string Extra { get; }

    public override IEnumerable<(string Section, Component Example)> Examples() =>
        Pickers().Select(example => (example.Section, Column(example.Picker, example.Section != "time-format")));

    private static IEnumerable<(string Section, Component Picker)> Pickers()
    {
        var now = FluxClock.Now;

        yield return ("", One(At(now.AddDays(5), 10, 30)));

        yield return ("range-picker", Range(new UiDateTimeRange(At(now.AddDays(5), 9, 0), At(now.AddDays(10), 17, 0))));

        yield return ("range-limits", Range().MaxRange(7));

        // The docs page hands this example a value of its own: two days on, at its server's time when it was drawn.
        yield return ("time-input", One(At(now.AddDays(2), 14, 47)).Time(Ui.DatetimePickerTime.Input));

        yield return ("time-input", Range(new UiDateTimeRange(At(now.AddDays(5), 9, 0), At(now.AddDays(10), 17, 0))).Time(Ui.DatetimePickerTime.Input));

        yield return ("interval", One().Interval(15));

        yield return ("default-time", One().DefaultTime(new TimeOnly(9, 0)));

        yield return ("min/max", One().Min(FluxClock.Moment).Max(At(now.AddDays(14), 17, 0)));

        yield return ("min/max-times", One().Min(FluxClock.Moment).MinTime(new TimeOnly(9, 0)).MaxTime(new TimeOnly(17, 0)));

        yield return ("unavailable-dates-and-times", One()
            .Min(FluxClock.Today.ToDateTime(TimeOnly.MinValue))
            .MinTime(new TimeOnly(8, 0))
            .MaxTime(new TimeOnly(18, 0))
            .Unavailable([
                now.AddDays(2),
                new UiTimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0)),
                At(now.AddDays(1), 10, 0),
                UiDatetimeSlot.On(now.AddDays(1), new TimeOnly(15, 0), new TimeOnly(16, 0)),
            ]));

        // Two pickers in a row, each in a field of its own.
        yield return ("time-format", Div.Style("display:flex;gap:16px;padding:0 8px")[
            One(At(now.AddDays(1), 18, 30)).Label("12-hour").TimeFormat(Ui.DatetimePickerTimeFormat.TwelveHour),
            One(At(now.AddDays(1), 18, 30)).Label("24-hour").TimeFormat(Ui.DatetimePickerTimeFormat.TwentyFourHour)
        ]);

        yield return ("clearable", One(At(now.AddDays(3), 9, 0)).Clearable());

        yield return ("with-confirmation", One().WithConfirmation());

        yield return ("custom-trigger", One().Trigger(Ui.Button.Icon(Ui.IconName.CalendarDays)["Schedule"]));

        yield return ("calendar-options", One().WithToday().SelectableHeader().WeekNumbers());

        yield return ("open-to", One().OpenTo(At(now.AddDays(10), 9, 0)));

        yield return ("localization", One(At(now.AddDays(3), 14, 0)).Locale("fr-FR"));

        // A date picker and a time picker side by side, as the docs page draws them.
        yield return ("separate-date-and-time", Div.Style("display:flex;gap:8px;max-width:384px")[
            Ui.DatePicker.Value(now.AddDays(5)).Locale("en-US").On(FluxClock.Today).Class("parity-flex-1"),
            Ui.TimePicker.Value((TimeOnly?)new TimeOnly(9, 30)).Locale("en-US").Class("parity-w-40")
        ]);
    }

    private static UiDatetimePicker One(DateTime value = default) => Ui.DatetimePicker.Value(value).Locale("en-US").On(FluxClock.Today).At(FluxClock.Time);

    private static UiDatetimePickerRange Range(UiDateTimeRange value = default) =>
        Ui.DatetimePicker.Range.Value(value).Locale("en-US").On(FluxClock.Today).At(FluxClock.Time);

    private static DateTime At(DateOnly day, int hour, int minute) => day.ToDateTime(new TimeOnly(hour, minute));

    // The docs page centres every picker in a 384px column, shrunk to the picker's own width.
    // The two pickers of "Time format" stand in a row of the docs page's own, straight in that column.
    private Component Column(Component picker, bool item) =>
        Div.Style(item ? "display:flex;justify-content:center;max-width:384px;margin:0 auto" : "display:flex;justify-content:center;margin:0 auto")[
            item ? Div[picker] : picker,
            Raw.Value(App + Extra)
        ];
}
