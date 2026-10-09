using System.Text.RegularExpressions;
using Rask.Testing;
using Rask.UiTests.Flux;
using Rask.Wire;

namespace Rask.UiTests.Components;

/// <summary>
///     A failure a submit handler throws is drawn by the kit's controls exactly as a rule's message is.
/// </summary>
public partial class UiFieldFailureTests : global::Rask.Core.RaskMarkup
{
    private const string Said = "Not this one.";

    [Fact]
    public async Task A_thrown_failure_is_drawn_exactly_as_a_rules_message_is()
    {
        var (ruled, thrown) = (new Trip(), new Trip());
        var byRule = Page.Render(() => Booking(ruled, rules: true, thrown: null));
        var byHandler = Page.Render(() => Booking(thrown, rules: false,
            new RefusedException(new FieldFailure(Said, ["Name", "Country", "Leaves", "Notes"]))));

        await byRule.On("form").Submit();
        await byHandler.On("form").Submit();

        Assert.Equal(4, byRule.FindAll("[aria-invalid=\"true\"]").Count);
        Assert.Equal(Drawn(byRule), Drawn(byHandler));
    }

    [Fact]
    public async Task A_field_a_failure_only_marks_is_invalid_and_says_nothing()
    {
        var trip = new Trip();
        var page = Page.Render(() => Booking(trip, rules: false,
            new RefusedException(new FieldFailure(Said, ["Country"], ["Leaves"]))));

        await page.On("form").Submit();

        Assert.Equal(2, page.FindAll("[aria-invalid=\"true\"]").Count);
        Assert.Contains(Said, page.TextOf("#trip-country-error"), StringComparison.Ordinal);
        Assert.Equal("", page.TextOf("#trip-leaves-error"));
    }

    // A date picker numbers its popup per process, which is not what is being compared.
    private static string Drawn(Page page) => PickerNumber().Replace(page.Render(), "ui-date-picker");

    [GeneratedRegex(@"ui-date-picker-\d+")]
    private static partial Regex PickerNumber();

    private static Rask.Core.Component Booking(Trip trip, bool rules, Exception? thrown)
    {
        return Form.Model(trip).OnSubmit(_ => thrown is null ? Task.CompletedTask : Task.FromException(thrown))[
            Ui.Input.Bind(() => trip.Name).Label("Name").Id("trip-name").Validate(_ => rules ? [Said] : []),
            Ui.Select.Bind(() => trip.Country).Label("Country").Id("trip-country").Validate(_ => rules ? [Said] : [])[
                Ui.SelectOption.Value("hu")["Hungary"],
                Ui.SelectOption.Value("gb")["United Kingdom"]
            ],
            Ui.DatePicker.Bind(() => trip.Leaves).Locale("en-US").On(new DateOnly(2026, 10, 9)).Label("Leaves").Id("trip-leaves").Validate(_ => rules ? [Said] : []),
            Ui.Field[
                Ui.Label["Notes"],
                Ui.Textarea.Bind(() => trip.Notes).Id("trip-notes").Validate(_ => rules ? [Said] : []),
                Ui.Error
            ],
            Ui.Button.Submit["Book"]
        ];
    }

    private sealed class RefusedException(params FieldFailure[] failures) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures => failures;
    }

    private sealed class Trip
    {
        public string Name { get; set; } = "Ada";
        public string Country { get; set; } = "hu";
        public DateOnly Leaves { get; set; } = new DateOnly(2026, 10, 20);
        public string Notes { get; set; } = "";
    }
}
