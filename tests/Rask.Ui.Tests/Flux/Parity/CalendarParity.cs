using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/calendar</c>, example by example.</summary>
/// <remarks>
///     Every calendar is told the day the measuring browser tells Flux's (<see cref="FluxClock" />) and the
///     locale that browser has; the dates an example is given are Flux's server's, which is on the real date.
/// </remarks>
public sealed partial class CalendarParity : FluxParity
{
    public override string Page => "calendar";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        var now = FluxClock.Now;

        yield return ("", Centered(Ui.Calendar.Value(default(DateOnly)).Locale("en-US").On(FluxClock.Today)));

        yield return ("multiple-dates", Centered(
            Ui.Calendar.Multiple.Values([FluxClock.Day(2), FluxClock.Day(5), FluxClock.Day(15)]).Locale("en-US").On(FluxClock.Today)));

        yield return ("date-range", Centered(
            Ui.Calendar.Range.Value(new UiDateRange(FluxClock.Day(2), FluxClock.Day(6))).Locale("en-US").On(FluxClock.Today)));

        yield return ("size", Centered(Ui.Calendar.Value(default(DateOnly)).Xl.Locale("en-US").On(FluxClock.Today)));

        yield return ("static", Centered(
            Ui.Calendar.Value(now).Static().Xs.Navigation(false).Locale("en-US").On(FluxClock.Today)));

        yield return ("min/max-dates", Centered(Ui.Calendar.Value(default(DateOnly)).Max(now).Locale("en-US").On(FluxClock.Today)));

        yield return ("unavailable-dates", Centered(
            Ui.Calendar.Value(default(DateOnly)).Unavailable([now.AddDays(-1), now.AddDays(1)]).Locale("en-US").On(FluxClock.Today)));

        yield return ("with-today-shortcut", Centered(
            Ui.Calendar.Value(default(DateOnly)).WithToday().Locale("en-US").On(FluxClock.Today)));

        yield return ("selectable-header", Centered(
            Ui.Calendar.Value(default(DateOnly)).SelectableHeader().Locale("en-US").On(FluxClock.Today)));

        yield return ("fixed-weeks", Centered(
            Ui.Calendar.Value(default(DateOnly)).FixedWeeks().Locale("en-US").On(FluxClock.Today)));

        yield return ("start-day", Centered(
            Ui.Calendar.Value(default(DateOnly)).StartDay(DayOfWeek.Monday).Locale("en-US").On(FluxClock.Today)));

        // Flux's page draws the first of its two open-to examples.
        yield return ("open-to", Centered(
            Ui.Calendar.Value(default(DateOnly)).OpenTo(new DateOnly(now.Year, now.Month, 1).AddMonths(1).AddYears(1))
                .Locale("en-US").On(FluxClock.Today)));

        yield return ("week-numbers", Centered(
            Ui.Calendar.Value(default(DateOnly)).WeekNumbers().Locale("en-US").On(FluxClock.Today)));

        yield return ("localization", Centered(Ui.Calendar.Value(default(DateOnly)).Locale("ja-JP").On(FluxClock.Today)));
    }

    // The docs page centres every calendar in a row, with 8px of room either side.
    private static Component Centered(Component calendar) =>
        Div.Style("display:flex;justify-content:center")[Div.Style("padding:0 8px")[calendar]];
}
