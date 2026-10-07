using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/date-picker</c>, example by example, as the page loads: every popup closed.</summary>
/// <remarks>
///     Every picker is told the day the measuring browser tells Flux's (<see cref="FluxClock" />) and the locale
///     that browser has; the dates an example is given are Flux's server's, which is on the real date.
/// </remarks>
public partial class DatePickerParity : FluxParity
{
    public override string Page => "date-picker";

    public override IEnumerable<(string Section, Component Example)> Examples() =>
        Pickers().Select(example => (example.Section, Column(example.Picker)));

    /// <summary>The fifteen pickers on Flux's page, under the section each stands in.</summary>
    protected static IEnumerable<(string Section, Component Picker)> Pickers()
    {
        var now = FluxClock.Now;

        yield return ("", Day());

        yield return ("input-trigger", Day().Type(Ui.DatePickerType.Input));

        yield return ("range-picker", Range());

        yield return ("range-limits", Range().MinRange(3));

        yield return ("range-limits", Range().MaxRange(10));

        // The row the two fields stand in is the example's own: Flux hands the slot a flex row with a 16px gap.
        yield return ("range-with-inputs", Range().Trigger(
            Div.Style("display:flex;gap:16px")[
                Ui.DatePickerInput.Label("Start"),
                Ui.DatePickerInput.Label("End")
            ]));

        // The docs page gives this one a minimum, for "All Time" to start from.
        yield return ("presets", Range().WithPresets().Min(new DateOnly(2012, 1, 1)));

        yield return ("unavailable-dates", Day().Unavailable([now.AddDays(-1), now.AddDays(1)]));

        yield return ("with-today-shortcut", Day().WithToday());

        yield return ("selectable-header", Day().SelectableHeader());

        yield return ("fixed-weeks", Day().FixedWeeks());

        yield return ("start-day", Day().StartDay(DayOfWeek.Monday));

        // Flux's page draws the first of its two open-to examples.
        yield return ("open-to", Day().OpenTo(new DateOnly(now.Year, now.Month, 1).AddMonths(1).AddYears(1)));

        yield return ("week-numbers", Day().WeekNumbers());

        // The docs page hands this one a class that lets go of the picker's 240px minimum: an app's own utility,
        // stated on the page under a name of its own.
        yield return ("localization", Day().Locale("ja-JP").Class("parity-narrow"));
    }

    private static UiDatePicker Day() => Ui.DatePicker.Value(default(DateOnly)).Locale("en-US").On(FluxClock.Today);

    private static UiDatePickerRange Range() => Ui.DatePicker.Range.Value(default(UiDateRange)).Locale("en-US").On(FluxClock.Today);

    /// <summary>
    ///     The docs site's own monospace face, which a typed date's segments are set in (<c>font-mono</c>) and
    ///     sized by (<c>ch</c>). It is a licensed font this page cannot load, so the name is given a local face
    ///     scaled to its advance: 0.64em, where Menlo and DejaVu Sans Mono are 0.602em.
    /// </summary>
    protected const string AppFont =
        "<style>@font-face{font-family:MonoLisa;src:local('Menlo'),local('Menlo Regular'),local('Menlo-Regular'),local('DejaVu Sans Mono'),local('DejaVuSansMono');size-adjust:106.3%}"
        + ":root{--font-mono:MonoLisa,monospace}.parity-narrow{min-width:0}</style>";

    // The docs page centres every picker in a 384px column, shrunk to the picker's own width.
    private static Component Column(Component picker) =>
        Div.Style("display:flex;justify-content:center;max-width:384px;margin:0 auto")[Div[picker], Raw.Value(AppFont)];
}
