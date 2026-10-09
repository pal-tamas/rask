using System.Text.RegularExpressions;
using Rask.Core.Forms;
using Rask.Testing;
using Rask.UiTests.Flux;
using Rask.Wire;

namespace Rask.UiTests.Components;

/// <summary>
///     What a store's rule or a property's own rule says, with no step on the field, is drawn by the kit's
///     controls exactly as a written rule's message is.
/// </summary>
public partial class UiStoreRuleTests : global::Rask.Core.RaskMarkup
{
    private const string Said = "Not this one.";

    static UiStoreRuleTests()
    {
        RaskValidation.RegisterStoreRules(typeof(Stored), _ => StoredRules.Instance);
        RaskValidation.RegisterFieldRules(typeof(Typed), Typed.RuleOf);
    }

    [Fact]
    public async Task What_the_store_says_on_submit_is_drawn_exactly_as_a_rules_message_is()
    {
        var (ruled, stored) = (new Trip(), new Stored());
        var byRule = Page.Render(() => Booking(ruled, rules: true));
        var byStore = Page.Render(() => Booking(stored, rules: false));

        await byRule.On("form").Submit();
        await byStore.On("form").Submit();

        Assert.Equal(4, byRule.FindAll("[aria-invalid=\"true\"]").Count);
        Assert.Equal(Drawn(byRule), Drawn(byStore));
    }

    [Fact]
    public async Task A_propertys_own_rule_is_drawn_exactly_as_a_written_rules_message_is()
    {
        var (ruled, typed) = (new Trip(), new Typed());
        var byRule = Page.Render(() => Booking(ruled, rules: true));
        var byType = Page.Render(() => Booking(typed, rules: false));

        await byRule.On("form").Submit();
        await byType.On("form").Submit();

        Assert.Equal(Drawn(byRule), Drawn(byType));
    }

    [Fact]
    public async Task What_the_store_says_as_an_input_is_committed_shows_under_it_and_marks_it()
    {
        var stored = new Stored();
        var page = Page.Render(() => Booking(stored, rules: false));

        await page.On("#trip-name").Change("Bea");

        Assert.Contains(Said, page.TextOf("#trip-name-error"), StringComparison.Ordinal);
        Assert.True(page.Exists("#trip-name[aria-invalid=\"true\"]"));
        Assert.Equal(["Name"], stored.Asked);
    }

    [Fact]
    public async Task A_range_failure_found_as_a_field_is_committed_marks_the_bound_and_says_nothing_under_it()
    {
        var stored = new Stored { Answer = [new FieldFailure(Said, ["Country"], ["Name"])] };
        var page = Page.Render(() => Booking(stored, rules: false));

        await page.On("#trip-name").Change("Bea");

        Assert.Contains(Said, page.TextOf("#trip-country-error"), StringComparison.Ordinal);
        Assert.Equal("", page.TextOf("#trip-name-error"));
        Assert.Equal(2, page.FindAll("[aria-invalid=\"true\"]").Count);
    }

    // A date picker numbers its popup per process, which is not what is being compared.
    private static string Drawn(Page page) => PickerNumber().Replace(page.Render(), "ui-date-picker");

    [GeneratedRegex(@"ui-date-picker-\d+")]
    private static partial Regex PickerNumber();

    private static Rask.Core.Component Booking<TTrip>(TTrip trip, bool rules)
        where TTrip : Trip
    {
        return Form.Model(trip)[
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

    private class Trip
    {
        public string Name { get; set; } = "Ada";
        public string Country { get; set; } = "hu";
        public DateOnly Leaves { get; set; } = new DateOnly(2026, 10, 20);
        public string Notes { get; set; } = "";
    }

    // A model a store registered rules for.
    private sealed class Stored : Trip
    {
        internal List<string?> Asked { get; } = [];

        internal IReadOnlyList<FieldFailure> Answer { get; init; } = [new FieldFailure(Said, ["Name", "Country", "Leaves", "Notes"])];
    }

    // A model whose every property carries a rule in its own type.
    private sealed class Typed : Trip
    {
        private static readonly Validate<string> Text = _ => [Said];
        private static readonly Validate<DateOnly> Date = _ => [Said];

        internal static Delegate? RuleOf(string property) => property == nameof(Leaves) ? Date : Text;
    }

    private sealed class StoredRules : IStoreRules
    {
        internal static readonly StoredRules Instance = new();

        public ValueTask<IReadOnlyList<FieldFailure>> Check(object model, string? field, CancellationToken cancellationToken)
        {
            var stored = (Stored)model;
            stored.Asked.Add(field);
            return new ValueTask<IReadOnlyList<FieldFailure>>(stored.Answer);
        }
    }
}
