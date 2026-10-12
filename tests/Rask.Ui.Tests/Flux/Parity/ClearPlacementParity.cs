using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     Every control with a clear button, holding a value, in the containers an app lays a form out with: for
///     <c>scripts/flux/clear-placement.mjs</c>, which holds each button to its trigger's box.
/// </summary>
/// <remarks>
///     <c>clear-placement</c> is no Flux slug: Flux's public pages draw one clear button, the input's, and never
///     in a row. The script measures that one on Flux's live page, in the same containers, and the rest against
///     the box they belong to — a button that leaves its trigger is wrong whatever Flux's numbers are.
/// </remarks>
public sealed partial class ClearPlacementParity : FluxParity
{
    // What an app's own sheet would say: the kit's sheet is compiled from the kit's sources alone.
    private const string App = "<style>.parity-grow{flex-grow:1}</style>";

    private const string Row = "display:flex;gap:8px";

    private static readonly DateOnly Day = new(2026, 1, 20);

    private static readonly TimeOnly At = new(9, 30);

    private static readonly string[] Words = ["Alabama", "Alaska", "Arizona"];

    public override string Page => "clear-placement";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return Cases("time-picker", (grow, label, locale) =>
            Ui.TimePicker.Value((TimeOnly?)At).Locale(locale).Clearable().Class(grow).Label(label));

        yield return Cases("time-picker-typed", (grow, label, locale) =>
            Ui.TimePicker.Value((TimeOnly?)At).Type(Ui.TimePickerType.Input).Locale(locale).Clearable().Class(grow).Label(label));

        yield return Cases("date-picker", (grow, label, locale) =>
            Ui.DatePicker.Value(Day).Locale(locale).On(FluxClock.Today).Clearable().Class(grow).Label(label));

        yield return Cases("date-picker-typed", (grow, label, locale) =>
            Ui.DatePicker.Value(Day).Type(Ui.DatePickerType.Input).Locale(locale).On(FluxClock.Today).Clearable().Class(grow).Label(label));

        yield return Cases("date-picker-range", (grow, label, locale) =>
            Ui.DatePicker.Range.Value(new UiDateRange(Day, Day.AddDays(3))).Locale(locale).On(FluxClock.Today).Clearable().Class(grow).Label(label));

        yield return Cases("select", (grow, label, _) =>
            Ui.Select.Value("Alaska").Listbox.Clearable().Class(grow).Label(label)[Words.Select(word => Ui.SelectOption.Key(word)[word])]);

        yield return Cases("combobox", (grow, label, _) =>
            Ui.Select.Value("Alaska").Combobox.Clearable().Class(grow).Label(label)[Words.Select(word => Ui.SelectOption.Key(word)[word])]);

        yield return Cases("input", (grow, label, _) => Ui.Input.Value("Alaska").Clearable().Class(grow).Label(label));

        yield return Cases("autocomplete", (grow, label, _) =>
            Ui.Autocomplete.Value("Alaska").Clearable().Class(grow).Label(label)[Words.Select(word => Ui.AutocompleteItem.Key(word)[word])]);

        yield return Cases("pillbox", (grow, label, _) =>
            Ui.Pillbox.Values(new List<string> { "Alaska" }).Class(grow).Label(label)[
                Ui.PillboxTrigger.Placeholder("Choose states...").Clearable(),
                Words.Select(word => Ui.PillboxOption.Key(word).Value(word)[word])
            ]);
    }

    // One control in each container. `draw` takes the class an app would grow it with, a label and a locale.
    private (string Section, Component Example) Cases(string control, Func<string?, string?, string, Component> draw)
    {
        Component Case(string name, string? style, params Component?[] items) =>
            Div.Data(new Dictionary<string, string?>(StringComparer.Ordinal) { ["clear-case"] = name, ["control"] = control })
                .Style("margin-bottom:24px;" + style)[items];

        return (control, Div[
            Raw.Value(App),
            Case("alone", null, draw(null, null, "en-US")),
            Case("hungarian", null, draw(null, null, "hu")),
            Case("labelled", null, draw(null, "When", "en-US")),
            // The row an app reported: a date picker that takes the room, the control beside it.
            Case("beside a date picker", Row, Neighbour(null), draw(null, null, "hu")),
            Case("beside a labelled date picker", Row, Neighbour("Day"), draw(null, null, "en-US")),
            Case("grown in a row", Row, Tall(), draw("parity-grow", null, "en-US")),
            Case("in a grid cell", "display:grid;grid-template-columns:1fr 1fr;gap:8px", Tall(), draw(null, null, "en-US")),
            Case("in a column", "display:flex;flex-direction:column;gap:8px", Tall(), draw(null, null, "en-US"))
        ]);
    }

    private static UiDatePicker Neighbour(string? label) =>
        Ui.DatePicker.Value(Day).Locale("hu").On(FluxClock.Today).Class("parity-grow").Label(label);

    // A neighbour three times the control's height: a row stretches the control's root to it.
    private Component Tall() => Div.Style("width:100px;height:120px;flex:none");
}
