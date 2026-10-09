using Rask.Core.Forms;
using Rask.Wire;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A property whose type carries its own rule — a value object's Validate — runs it with no step on the
// field: first, on the field's own bind timing, before the steps the field does write and before the store.
public partial class FieldRuleTests : global::Rask.Core.RaskMarkup
{
    private const string Taken = "That name is taken.";

    static FieldRuleTests()
    {
        RaskValidation.RegisterFieldRules(typeof(Stop), Stop.RuleOf);
        RaskValidation.RegisterFieldRules(typeof(Platform), Platform.RuleOf);
        RaskValidation.RegisterStoreRules(typeof(Stop), _ => StopStore.Instance);
        RaskValidation.RegisterFieldRules(typeof(Miswired), _ => new Validate<int>(_ => []));
    }

    [Fact]
    public async Task A_live_field_runs_its_propertys_rule_on_every_keystroke_once_touched()
    {
        var stop = new Stop();
        var (page, form) = Bound(stop, () => Input.Bind(() => stop.Name).Id("name"));
        await page.On("#name").Change("B");

        await page.On("#name").Input("Bu");
        var whileShort = Messages(form, stop, "Name");
        await page.On("#name").Input("Buda");

        Assert.Equal(["Name is too short."], whileShort);
        Assert.Empty(Messages(form, stop, "Name"));
    }

    [Fact]
    public async Task A_debounced_field_runs_its_propertys_rule_at_the_pause()
    {
        var stop = new Stop();
        var (page, form) = Bound(stop, () => Input.Bind(() => stop.Name).Id("name").Debounce(300.Milliseconds));

        await page.On("#name").Input("Bu");

        Assert.Equal(["Name is too short."], Messages(form, stop, "Name"));
    }

    [Fact]
    public async Task A_step_written_on_the_field_runs_after_the_propertys_rule_and_only_once_it_accepted()
    {
        var stop = new Stop();
        var written = new List<string>();
        var (page, form) = Bound(stop, () => Input.Bind(() => stop.Name).Id("name").Blur().Validate(v =>
        {
            written.Add(v);
            return v.StartsWith('B') ? [] : ["Name starts with B."];
        }));

        await page.On("#name").Change("Wi");
        var refusedByTheType = Messages(form, stop, "Name");
        await page.On("#name").Change("Wien");

        Assert.Equal(["Name is too short."], refusedByTheType);
        Assert.Equal(["Name starts with B."], Messages(form, stop, "Name"));
        Assert.Equal(["Wien"], written);
    }

    [Fact]
    public async Task The_store_is_asked_only_after_the_propertys_rule_and_the_written_step_both_accepted()
    {
        var stop = new Stop();
        stop.Store = (_, _) => [new FieldFailure(Taken, ["Name"])];
        var (page, form) = Bound(stop, () => Input.Bind(() => stop.Name).Id("name").Blur()
            .Validate(v => v.StartsWith('B') ? [] : ["Name starts with B."]));

        await page.On("#name").Change("Wi");
        await page.On("#name").Change("Wien");
        var askedBeforeBothAccepted = stop.StoreAsked.Count;
        await page.On("#name").Change("Buda");

        Assert.Equal(0, askedBeforeBothAccepted);
        Assert.Equal(["Name"], stop.StoreAsked);
        Assert.Equal([Taken], Messages(form, stop, "Name"));
    }

    [Fact]
    public async Task A_submit_runs_the_propertys_rule_and_keeps_the_save_from_running()
    {
        var stop = new Stop { Name = "Bu" };
        var (page, form) = Bound(stop, () => Input.Bind(() => stop.Name).Id("name"));

        await page.On("form").Submit("""{"form":{}}""");

        Assert.Equal(["Name is too short."], Messages(form, stop, "Name"));
        Assert.Equal(0, stop.Saves);
        Assert.Empty(stop.StoreAsked);
    }

    [Fact]
    public async Task A_form_that_turns_its_automatic_validation_off_still_runs_the_propertys_rule()
    {
        var stop = new Stop();
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(stop).AutoValidate(false)[
            Input.Bind(() => stop.Name).Id("name").Blur(), Test.EditContextProbe(c => form = c)
        ]);

        await page.On("#name").Change("Bu");

        Assert.Equal(["Name is too short."], Messages(form!, stop, "Name"));
    }

    [Fact]
    public async Task A_row_nested_in_the_model_runs_the_rule_its_own_type_registered()
    {
        var stop = new Stop();
        var (page, form) = Bound(stop, () => Input.Bind(() => stop.Platforms[1].Sign).Id("sign").Blur());

        await page.On("#sign").Change("");

        Assert.Equal(["Sign is required."], Messages(form, stop.Platforms[1], "Sign"));
    }

    [Fact]
    public async Task A_property_with_no_rule_of_its_own_keeps_only_what_the_field_wrote()
    {
        var stop = new Stop();
        var (page, form) = Bound(stop, () => Input.Bind(() => stop.Town).Id("town").Blur());

        await page.On("#town").Change("");

        Assert.Empty(Messages(form, stop, "Town"));
    }

    [Fact]
    public void A_model_nobody_registered_keeps_the_very_rule_its_field_wrote_and_gains_none()
    {
        var plain = new Plain();
        Validate<string> written = _ => [];
        var none = RaskValidation.RuleFor<string>(new FieldIdentifier(plain, "Name"), null);

        var kept = RaskValidation.RuleFor<string>(new FieldIdentifier(plain, "Name"), written);

        Assert.Null(none);
        Assert.Same(written, kept);
    }

    [Fact]
    public void A_property_with_a_rule_and_no_written_step_registers_the_generators_own_delegate()
    {
        var stop = new Stop();

        var first = RaskValidation.RuleFor<string>(new FieldIdentifier(stop, "Name"), null);
        var second = RaskValidation.RuleFor<string>(new FieldIdentifier(stop, "Name"), null);

        Assert.Same(first, second);
    }

    [Fact]
    public void A_rule_registered_over_another_type_than_the_field_is_bound_as_says_so()
    {
        var miswired = new Miswired();

        var thrown = Assert.Throws<InvalidOperationException>(
            () => RaskValidation.RuleFor<string>(new FieldIdentifier(miswired, "Name"), null));

        Assert.Contains("Validate<String>", thrown.Message, StringComparison.Ordinal);
    }

    private static (Page Page, EditContext Form) Bound(Stop stop, Func<Component> field)
    {
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(stop).OnSubmit(_ => stop.Saves++)[field(), Test.EditContextProbe(c => form = c)]);
        return (page, form!);
    }

    // A copy: the form hands back the list it keeps, which the next validation rewrites.
    private static IReadOnlyList<string> Messages(EditContext form, object owner, string name) =>
        [.. form.GetValidationMessages(new FieldIdentifier(owner, name))];

    // What a generator emits for a model: one kept delegate per property whose type has a Validate.
    private sealed class Stop
    {
        private static readonly Validate<string> NameRule = value => value.Length < 3 ? ["Name is too short."] : [];

        public string Name { get; set; } = "";
        public string Town { get; set; } = "";
        public List<Platform> Platforms { get; set; } = [new(), new()];

        internal List<string?> StoreAsked { get; } = [];

        internal Func<Stop, string?, IReadOnlyList<FieldFailure>> Store { get; set; } = (_, _) => [];

        internal int Saves { get; set; }

        internal static Delegate? RuleOf(string property) => property == nameof(Name) ? NameRule : null;
    }

    private sealed class Platform
    {
        private static readonly Validate<string> SignRule = value => value.Length == 0 ? ["Sign is required."] : [];

        public string Sign { get; set; } = "A";

        internal static Delegate? RuleOf(string property) => property == nameof(Sign) ? SignRule : null;
    }

    private sealed class Plain
    {
        public string Name { get; set; } = "";
    }

    private sealed class Miswired
    {
        public string Name { get; set; } = "";
    }

    private sealed class StopStore : IStoreRules
    {
        internal static readonly StopStore Instance = new();

        public ValueTask<IReadOnlyList<FieldFailure>> Check(object model, string? field, CancellationToken cancellationToken)
        {
            var stop = (Stop)model;
            stop.StoreAsked.Add(field);
            return new ValueTask<IReadOnlyList<FieldFailure>>(stop.Store(stop, field));
        }
    }
}
