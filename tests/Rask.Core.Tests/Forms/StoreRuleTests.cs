using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Wire;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A rule the store owns — a unique index, a range that must not overlap — is asked as a field is committed
// and again on submit, with no step on the field and none on the form. The store here is a fake each trip
// carries, so every test owns its own. The collection is the one that owns the process-wide diagnostics sink.
[Collection("ConsoleRedirect")]
public partial class StoreRuleTests : global::Rask.Core.RaskMarkup
{
    private const string Taken = "Ilyen néven már létezik viszonylat.";
    private const string Booked = "This driver is already booked then.";

    static StoreRuleTests() => RaskValidation.RegisterStoreRules(typeof(Trip), _ => TripRules.Instance);

    private static IEnumerable<string> AtLeastThree(string value) =>
        value.Length < 3 ? ["too short"] : [];

    [Fact]
    public async Task Typing_into_a_field_sent_at_every_keystroke_never_asks_the_store()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip)[Input.Bind(() => trip.Name).Id("name").Debounce(TimeSpan.Zero)]);
        await page.On("#name").Change("B");
        trip.Store.Asked.Clear();

        await page.On("#name").Input("Bu");
        await page.On("#name").Input("Buda");

        Assert.Empty(trip.Store.Asked);
    }

    // A masked input rewrites what was typed from its AfterBind and validates the field again.
    [Fact]
    public async Task An_AfterBind_that_validates_the_field_while_it_is_typed_into_does_not_ask_the_store_either()
    {
        var trip = new Trip();
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(trip)[
            Input.Bind(() => trip.Name).Id("name").Debounce(TimeSpan.Zero)
                .AfterBind(_ => BindingHelpers.NotifyAndValidateField(form, new FieldIdentifier(trip, "Name"))),
            Test.EditContextProbe(c => form = c)
        ]);

        await page.On("#name").Input("Buda");
        var askedWhileTyping = trip.Store.Asked.Count;
        await page.On("#name").Change("Buda");

        Assert.Equal(0, askedWhileTyping);
        Assert.Equal(["Name"], trip.Store.Asked);
    }

    [Fact]
    public async Task Leaving_a_field_sent_at_every_keystroke_asks_the_store_once_naming_the_field()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip)[Input.Bind(() => trip.Name).Id("name").Debounce(TimeSpan.Zero)]);
        await page.On("#name").Input("Buda");

        await page.On("#name").Change("Buda");

        Assert.Equal(["Name"], trip.Store.Asked);
    }

    // A plain bound field says nothing until the next action. Its value arriving then is its commit.
    [Fact]
    public async Task The_value_of_a_field_that_waited_for_the_action_asks_the_store_once_when_it_arrives()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip)[Input.Bind(() => trip.Name).Id("name")]);

        await page.On("#name").Change("Buda");

        Assert.Equal("action", page.Attr("data-rask-bind-on"));
        Assert.Equal(["Name"], trip.Store.Asked);
    }

    [Fact]
    public async Task The_pause_of_a_live_field_asks_the_store_once()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip)[Input.Bind(() => trip.Name).Id("name").Live()]);

        await page.On("#name").Input("Buda");

        Assert.Equal(["Name"], trip.Store.Asked);
    }

    [Fact]
    public async Task The_pause_of_a_debounced_field_asks_the_store_once()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip)[Input.Bind(() => trip.Name).Id("name").Debounce(300.Milliseconds)]);

        await page.On("#name").Input("Buda");

        Assert.Equal(["Name"], trip.Store.Asked);
    }

    [Fact]
    public async Task Leaving_a_field_bound_on_blur_asks_the_store_once()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip)[Input.Bind(() => trip.Name).Id("name").Blur()]);

        await page.On("#name").Change("Buda");

        Assert.Equal(["Name"], trip.Store.Asked);
    }

    [Fact]
    public async Task A_select_and_a_checkbox_ask_the_store_when_they_change()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip)[
            Select.Bind(() => trip.Driver).Id("driver")[Option.Value("Ada")["Ada"], Option.Value("Bea")["Bea"]],
            Input.Bind(() => trip.Night).Id("night")
        ]);

        await page.On("#driver").Change("Bea");
        await page.On("#night").Change("true");

        Assert.Equal(["Driver", "Night"], trip.Store.Asked);
    }

    [Fact]
    public async Task The_store_is_not_asked_while_a_rule_on_the_field_is_failing()
    {
        var trip = new Trip();
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(trip)[
            Input.Bind(() => trip.Name).Id("name").Debounce(300.Milliseconds).Validate(AtLeastThree),
            Test.EditContextProbe(c => form = c)
        ]);

        await page.On("#name").Input("Bu");

        Assert.Empty(trip.Store.Asked);
        Assert.Equal(["too short"], Messages(form!, trip, "Name"));
    }

    [Fact]
    public async Task The_store_is_asked_after_a_validator_added_to_the_form_later_and_not_once_that_one_fails()
    {
        var trip = new Trip();
        var (page, form) = Filled(trip);
        form.AddValidator(new RefusesEverything());

        await page.On("#name").Change("Buda");

        Assert.Empty(trip.Store.Asked);
        Assert.Equal(["refused"], Messages(form, trip, "Name"));
    }

    [Fact]
    public async Task What_the_store_says_about_one_field_is_shown_under_it_like_any_rule_message()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Taken, ["Name"])];
        var (page, form) = Filled(trip);

        await page.On("#name").Change("Buda");

        Assert.Equal([Taken], Messages(form, trip, "Name"));
        Assert.Equal(Taken, page.TextOf("p.name"));
    }

    [Fact]
    public async Task A_failure_over_two_fields_is_shown_under_each()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Taken, ["Year", "Number"])];
        var (page, form) = Filled(trip);

        await page.On("#number").Change("42");

        Assert.Equal([Taken], Messages(form, trip, "Year"));
        Assert.Equal([Taken], Messages(form, trip, "Number"));
        Assert.Equal(Taken, page.TextOf("p.year"));
    }

    [Fact]
    public async Task A_range_failure_is_told_under_the_field_it_is_about_and_only_marks_its_bounds()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Booked, ["Driver"], ["From", "To"])];
        var (page, form) = Filled(trip);

        await page.On("#to").Change("2026-11-02");

        Assert.Equal([Booked], Messages(form, trip, "Driver"));
        Assert.Empty(Messages(form, trip, "From"));
        Assert.Empty(Messages(form, trip, "To"));
        Assert.True(form.IsInvalid(new FieldIdentifier(trip, "From")));
        Assert.True(form.IsInvalid(new FieldIdentifier(trip, "To")));
    }

    // Core's own controls say so themselves: no kit component is on this page.
    [Fact]
    public async Task A_plain_bound_input_is_aria_invalid_while_it_holds_a_message_or_is_only_marked()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Booked, ["Driver"], ["From", "To"])];
        var (page, _) = Filled(trip);
        var before = page.FindAll("[aria-invalid]").Count;

        await page.On("#to").Change("2026-11-02");
        var held = page.FindAll("input[aria-invalid=\"true\"]").Select(input => input.Id).ToArray();
        await page.On("#from").Raise("edit", """{"type":"edit"}""");

        Assert.Equal(0, before);
        Assert.Equal(["driver", "from", "to"], held);
        Assert.Empty(page.FindAll("[aria-invalid]"));
    }

    [Fact]
    public async Task A_plain_bound_select_and_textarea_are_aria_invalid_while_their_rule_fails_and_no_longer_after()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip)[
            Select.Bind(() => trip.Driver).Id("driver").Validate(v => v == "Bea" ? ["Not Bea."] : [])[
                Option.Value("Ada")["Ada"], Option.Value("Bea")["Bea"]],
            Textarea.Bind(() => trip.Name).Id("notes").Blur().Validate(AtLeastThree)
        ]);

        await page.On("#driver").Change("Bea");
        await page.On("#notes").Change("Bu");
        var held = page.FindAll("[aria-invalid=\"true\"]").Count;
        await page.On("#driver").Change("Ada");
        await page.On("#notes").Change("Buda");

        Assert.Equal(2, held);
        Assert.Empty(page.FindAll("[aria-invalid]"));
    }

    [Fact]
    public async Task An_aria_invalid_the_author_wrote_on_a_bound_input_is_left_as_written()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip)[
            Input.Bind(() => trip.Name).Id("name").Blur().AriaInvalid(AriaInvalid.Spelling).Validate(AtLeastThree)
        ]);

        await page.On("#name").Change("Bu");
        await page.On("#name").Change("Buda");

        Assert.True(page.Exists("#name[aria-invalid=\"spelling\"]"));
    }

    [Fact]
    public async Task A_field_of_a_nested_object_and_of_a_row_is_named_by_its_path()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip)[
            Input.Bind(() => trip.Price.Amount).Id("amount").Blur(),
            Div[trip.Legs.Select((leg, i) => (Component)Div.Key(i)[Input.Bind(() => leg.From).Id($"leg{i}").Blur()])]
        ]);

        await page.On("#amount").Change("12");
        await page.On("#leg2").Change("2026-10-05");

        Assert.Equal(["Price.Amount", "Legs[2].From"], trip.Store.Asked);
    }

    [Fact]
    public async Task The_next_check_of_a_field_replaces_what_its_last_check_found_under_another_field()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Taken, ["Number"])];
        var (page, form) = Filled(trip);
        await page.On("#year").Change("2025");

        trip.Store.Answer = (_, _) => [];
        await page.On("#year").Change("2026");

        Assert.Empty(Messages(form, trip, "Number"));
        page.DoesNotShow(Taken);
    }

    [Fact]
    public async Task The_first_keystroke_of_a_correction_clears_the_failure_from_every_field_it_names()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Taken, ["Year", "Number"])];
        var (page, form) = Filled(trip);
        await page.On("#number").Change("42");

        await page.On("#year").Raise("edit", """{"type":"edit"}""");

        Assert.Empty(Messages(form, trip, "Year"));
        Assert.Empty(Messages(form, trip, "Number"));
        page.DoesNotShow(Taken);
        Assert.Equal(["Number"], trip.Store.Asked);
    }

    [Fact]
    public async Task A_field_a_failure_only_marks_hears_the_first_keystroke_too_and_takes_the_message_away()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Booked, ["Driver"], ["From", "To"])];
        var (page, form) = Filled(trip);
        await page.On("#to").Change("2026-11-02");

        await page.On("#from").Raise("edit", """{"type":"edit"}""");

        Assert.Empty(Messages(form, trip, "Driver"));
        Assert.False(form.IsInvalid(new FieldIdentifier(trip, "To")));
    }

    [Fact]
    public async Task Typing_into_a_field_sent_at_every_keystroke_clears_what_the_store_said_without_asking_again()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Taken, ["Name"])];
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(trip)[Input.Bind(() => trip.Name).Id("name").Debounce(TimeSpan.Zero), Test.EditContextProbe(c => form = c)]);
        await page.On("#name").Change("Buda");

        await page.On("#name").Input("Budap");

        Assert.Empty(Messages(form!, trip, "Name"));
        Assert.Equal(["Name"], trip.Store.Asked);
    }

    [Fact]
    public async Task A_value_equal_to_the_one_last_checked_is_not_asked_about_again_and_shows_the_same_answer()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Taken, ["Name"])];
        var (page, form) = Filled(trip);
        await page.On("#name").Change("Buda");
        await page.On("#name").Raise("edit", """{"type":"edit"}""");

        await page.On("#name").Change("Buda");

        Assert.Equal(["Name"], trip.Store.Asked);
        Assert.Equal([Taken], Messages(form, trip, "Name"));
    }

    [Fact]
    public async Task A_field_is_asked_about_again_once_another_field_has_changed()
    {
        var trip = new Trip();
        var (page, _) = Filled(trip);
        await page.On("#year").Change("2026");
        await page.On("#number").Change("43");

        await page.On("#year").Change("2026");

        Assert.Equal(["Year", "Number", "Year"], trip.Store.Asked);
    }

    // Two fields of one rule committed in one batch arrive as two handler turns: the store is asked once for
    // each, the second answer replaces the first, and the failure is on the form once.
    [Fact]
    public async Task Two_fields_of_one_rule_committed_together_ask_twice_and_leave_one_failure()
    {
        var trip = new Trip();
        trip.Store.Answer = (model, _) => model is { Year: "2026", Number: "42" } ? [new FieldFailure(Taken, ["Year", "Number"])] : [];
        var (page, form) = Filled(trip);

        await page.On("#year").Change("2026");
        await page.On("#number").Change("42");

        Assert.Equal(["Year", "Number"], trip.Store.Asked);
        Assert.Equal([Taken], Messages(form, trip, "Year"));
        Assert.Equal([Taken], Messages(form, trip, "Number"));
    }

    [Fact]
    public async Task A_submit_asks_about_the_whole_model_and_a_failure_keeps_the_save_from_running()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Taken, ["Name"])];
        var (page, form) = Filled(trip);

        await page.On("form").Submit("""{"form":{}}""");

        Assert.Equal([null], trip.Store.Asked);
        Assert.Equal(0, trip.Saves);
        Assert.Equal([Taken], Messages(form, trip, "Name"));
    }

    [Fact]
    public async Task A_submit_the_store_accepts_runs_the_save()
    {
        var trip = new Trip();
        var (page, _) = Filled(trip);

        await page.On("form").Submit("""{"form":{}}""");

        Assert.Equal([null], trip.Store.Asked);
        Assert.Equal(1, trip.Saves);
    }

    [Fact]
    public async Task A_submit_with_a_failing_rule_of_the_forms_own_does_not_ask_the_store()
    {
        var trip = new Trip { Name = "Bu" };
        var page = Page.Render(() => Form.Model(trip).OnSubmit(_ => trip.Saves++)[
            Input.Bind(() => trip.Name).Id("name").Validate(AtLeastThree)
        ]);

        await page.On("form").Submit("""{"form":{}}""");

        Assert.Empty(trip.Store.Asked);
        Assert.Equal(0, trip.Saves);
    }

    [Fact]
    public async Task A_failure_found_on_submit_stays_until_the_value_changes_as_a_refused_save_does()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => [new FieldFailure(Taken, ["Name"])];
        var (page, form) = Filled(trip);
        await page.On("form").Submit("""{"form":{}}""");

        var valid = await form.ValidateField(new FieldIdentifier(trip, "Name"), TestContext.Current.CancellationToken);

        Assert.False(valid);
        Assert.Equal([Taken], Messages(form, trip, "Name"));
    }

    [Fact]
    public async Task A_form_that_turns_its_automatic_validation_off_still_asks_the_store()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip).AutoValidate(false)[Input.Bind(() => trip.Name).Id("name").Blur()]);

        await page.On("#name").Change("Buda");

        Assert.Equal(["Name"], trip.Store.Asked);
    }

    [Fact]
    public void A_model_no_store_registered_rules_for_gains_no_validator_to_await()
    {
        var plain = new Plain();
        EditContext? form = null;

        Page.Render(() => Form.Model(plain)[Input.Bind(() => plain.Name), Test.EditContextProbe(c => form = c)]);

        Assert.False(RaskValidation.HasStoreRulesFor(typeof(Plain)));
        Assert.False(form!.HasAsyncValidators);
    }

    [Fact]
    public void A_model_with_registered_rules_gains_exactly_one_validator_however_often_it_renders()
    {
        var trip = new Trip();
        var (page, form) = Filled(trip);

        page.Render();
        page.Render();

        Assert.True(RaskValidation.HasStoreRulesFor(typeof(Trip)));
        Assert.True(form.HasAsyncValidator(typeof(StoreRuleValidator)));
        Assert.False(RaskValidation.HasStoreRulesFor(typeof(TripOnTour)));
    }

    [Fact]
    public async Task A_later_commit_of_the_same_field_wins_over_a_check_still_running()
    {
        var trip = new Trip();
        var (_, form) = Filled(trip);
        var name = new FieldIdentifier(trip, "Name");
        var first = trip.Store.Hold();
        trip.Name = "Buda";
        trip.Store.Answer = (_, _) => [new FieldFailure(Taken, ["Name"])];
        var superseded = form.ValidateField(name, TestContext.Current.CancellationToken);

        trip.Name = "Pest";
        trip.Store.Answer = (_, _) => [];
        trip.Store.Release();
        var latest = await form.ValidateField(name, TestContext.Current.CancellationToken);
        first.TrySetResult();

        Assert.False(await superseded);
        Assert.True(latest);
        Assert.Empty(Messages(form, trip, "Name"));
    }

    [Fact]
    public async Task A_check_cancelled_because_its_field_went_away_leaves_no_message_and_reports_nothing()
    {
        var trip = new Trip();
        var (_, form) = Filled(trip);
        trip.Store.Hold();
        using var lifetime = new CancellationTokenSource();
        ValueTask<bool> check;
        using (Ambient.Enter(lifetime.Token))
        {
#pragma warning disable xUnit1051 // No token on purpose: a handler passes none, and the check must stop with the work it runs in.
            check = form.ValidateField(new FieldIdentifier(trip, "Name"));
#pragma warning restore xUnit1051
        }

        var reported = await Reported(async () =>
        {
            await lifetime.CancelAsync();
            await check;
        });

        Assert.False(form.HasValidationMessages());
        Assert.Empty(reported);
        Assert.Equal(["Name"], trip.Store.Asked);
    }

    [Fact]
    public async Task A_store_that_cannot_answer_says_nothing_on_the_form_and_lets_the_save_run()
    {
        var trip = new Trip();
        trip.Store.Answer = (_, _) => throw new InvalidOperationException("the database is away");
        var (page, form) = Filled(trip);

        var reported = await Reported(async () =>
        {
            await page.On("#name").Change("Buda");
            await page.On("form").Submit("""{"form":{}}""");
        });

        Assert.False(form.HasValidationMessages());
        Assert.Equal(1, trip.Saves);
        Assert.All(reported, e => Assert.Equal(RaskLogLevel.Warning, e.Level));
        Assert.Equal(2, reported.Count);
    }

    [Fact]
    public async Task A_store_with_nothing_to_ask_here_is_skipped()
    {
        var absent = new Absent();
        RaskValidation.RegisterStoreRules(typeof(Absent), _ => null);
        var page = Page.Render(() => Form.Model(absent).OnSubmit(_ => absent.Saves++)[Input.Bind(() => absent.Name).Id("name").Blur()]);

        await page.On("#name").Change("Buda");
        await page.On("form").Submit("""{"form":{}}""");

        Assert.Equal(1, absent.Saves);
    }

    // The form every test fills in: waiting fields, a message under two of them, the edit context in hand.
    private static (Page Page, EditContext Form) Filled(Trip trip)
    {
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(trip).OnSubmit(_ => trip.Saves++)[
            Input.Bind(() => trip.Name).Id("name").Blur(),
            Validation.Message.Template(messages => P.Class("name")[messages[0]]).For(() => trip.Name),
            Input.Bind(() => trip.Year).Id("year").Blur(),
            Validation.Message.Template(messages => P.Class("year")[messages[0]]).For(() => trip.Year),
            Input.Bind(() => trip.Number).Id("number").Blur(),
            Input.Bind(() => trip.Driver).Id("driver").Blur(),
            Input.Bind(() => trip.From).Id("from").Blur(),
            Input.Bind(() => trip.To).Id("to").Blur(),
            Test.EditContextProbe(c => form = c)
        ]);
        return (page, form!);
    }

    private static IReadOnlyList<string> Messages(EditContext form, object owner, string name) =>
        form.GetValidationMessages(new FieldIdentifier(owner, name));

    private static async Task<List<RaskDiagnosticEvent>> Reported(Func<Task> act)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;
        try
        {
            await act();
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        return captured;
    }

    private class Trip
    {
        public string Name { get; set; } = "Wien";
        public string Year { get; set; } = "2026";
        public string Number { get; set; } = "41";
        public string Driver { get; set; } = "Ada";
        public string From { get; set; } = "2026-10-01";
        public string To { get; set; } = "2026-10-31";
        public bool Night { get; set; }
        public Money Price { get; set; } = new();
        public List<Leg> Legs { get; set; } = [new(), new(), new()];

        internal FakeStore Store { get; } = new();

        internal int Saves { get; set; }
    }

    // Derived from a model with rules, and not one itself: the registration names a type exactly.
    private sealed class TripOnTour : Trip;

    private sealed class Money
    {
        public string Amount { get; set; } = "10";
    }

    private sealed class Leg
    {
        public string From { get; set; } = "2026-10-01";
    }

    private sealed class Plain
    {
        public string Name { get; set; } = "";
    }

    private sealed class Absent
    {
        public string Name { get; set; } = "";

        internal int Saves { get; set; }
    }

    // What the store registered for Trip resolves to: it sends each question to the fake the trip carries.
    private sealed class TripRules : IStoreRules
    {
        internal static readonly TripRules Instance = new();

        public ValueTask<IReadOnlyList<FieldFailure>> Check(object model, string? field, CancellationToken cancellationToken) =>
            ((Trip)model).Store.Check((Trip)model, field, cancellationToken);
    }

    private sealed class FakeStore
    {
        private TaskCompletionSource? _held;

        internal List<string?> Asked { get; } = [];

        internal Func<Trip, string?, IReadOnlyList<FieldFailure>> Answer { get; set; } = (_, _) => [];

        /// <summary>Makes the next checks wait until released, or until their own cancellation.</summary>
        internal TaskCompletionSource Hold() => _held = new TaskCompletionSource();

        internal void Release() => _held = null;

        internal async ValueTask<IReadOnlyList<FieldFailure>> Check(Trip trip, string? field, CancellationToken cancellationToken)
        {
            Asked.Add(field);
            var answer = Answer;
            if (_held is { } held)
            {
                await held.Task.WaitAsync(cancellationToken);
            }

            return answer(trip, field);
        }
    }

    private sealed class RefusesEverything : IAsyncFieldValidator
    {
        public ValueTask Validate(EditContext context, CancellationToken cancellationToken) => default;

        public ValueTask ValidateField(EditContext context, FieldIdentifier field, CancellationToken cancellationToken)
        {
            context.AddValidationMessage(field, "refused");
            return default;
        }
    }
}
