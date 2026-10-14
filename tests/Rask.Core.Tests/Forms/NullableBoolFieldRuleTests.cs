using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A generated form model holds an aggregate's `bool Paid` as `bool? Paid`, with the required rule over `bool?`. A
// checkbox bound to it — as the bool? itself, or lifted to the bool a control over a value type binds — renders,
// writes what it is ticked to, and an unticked box is a value the rule accepts.
public partial class NullableBoolFieldRuleTests : global::Rask.Core.RaskMarkup
{
    private const string Required = "This field is required.";

    static NullableBoolFieldRuleTests()
    {
        RaskValidation.RegisterFieldRules(typeof(InvoiceModel), InvoiceModel.RuleOf);
    }

    public static TheoryData<bool> Binds => [false, true];

    [Theory]
    [MemberData(nameof(Binds))]
    public async Task A_checkbox_over_a_nullable_bool_with_its_rule_writes_true_when_ticked_and_false_when_cleared(bool lifted)
    {
        var invoice = new InvoiceModel();
        var (page, form) = Bound(invoice, lifted);

        await page.On("#paid").Change("true");
        var ticked = invoice.Paid;
        await page.On("#paid").Change("false");

        Assert.True(ticked);
        Assert.False(invoice.Paid);
        Assert.Empty(Messages(form, invoice));
    }

    [Theory]
    [MemberData(nameof(Binds))]
    public async Task An_unticked_checkbox_over_a_nullable_bool_is_accepted_on_submit(bool lifted)
    {
        var invoice = new InvoiceModel();
        var (page, form) = Bound(invoice, lifted);

        await page.On("form").Submit("""{"form":{}}""");

        Assert.Equal(1, invoice.Saves);
        Assert.Empty(Messages(form, invoice));
    }

    [Theory]
    [MemberData(nameof(Binds))]
    public async Task A_checkbox_over_a_nullable_bool_that_holds_nothing_is_refused_by_its_rule_on_submit(bool lifted)
    {
        var invoice = new InvoiceModel { Paid = null };
        var (page, form) = Bound(invoice, lifted);

        await page.On("form").Submit("""{"form":{}}""");

        Assert.Equal(0, invoice.Saves);
        Assert.Equal([Required], Messages(form, invoice));
    }

    [Fact]
    public async Task A_step_written_over_bool_on_a_lifted_checkbox_runs_after_the_rule_over_bool_nullable()
    {
        var invoice = new InvoiceModel();
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(invoice)[
            Input.Bind(ExpressionAccessor.NonNullable(() => invoice.Paid)).Id("paid")
                .Validate(paid => paid ? [] : ["Tick it."]),
            Test.EditContextProbe(c => form = c)
        ]);

        await page.On("#paid").Change("false");
        var refused = Messages(form!, invoice);
        await page.On("#paid").Change("true");

        Assert.Equal(["Tick it."], refused);
        Assert.Empty(Messages(form!, invoice));
    }

    [Fact]
    public void A_step_written_over_bool_is_not_asked_about_a_property_that_holds_nothing()
    {
        var invoice = new InvoiceModel { Paid = null };
        var asked = 0;
        Validate<bool> written = _ =>
        {
            asked++;
            return [];
        };
        var rule = RaskValidation.RuleFor<bool>(new FieldIdentifier(invoice, "Paid"), written)!;

        var messages = DelegateValidator.InvokeSync(rule, invoice.Paid);

        Assert.Equal([Required], messages);
        Assert.Equal(0, asked);
    }

    private static (Page Page, EditContext Form) Bound(InvoiceModel invoice, bool lifted)
    {
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(invoice).OnSubmit(_ => invoice.Saves++)[
            lifted
                ? Input.Bind(ExpressionAccessor.NonNullable(() => invoice.Paid)).Id("paid")
                : Input.Bind(() => invoice.Paid).Id("paid"),
            Test.EditContextProbe(c => form = c)
        ]);
        return (page, form!);
    }

    private static IReadOnlyList<string> Messages(EditContext form, InvoiceModel invoice) =>
        [.. form.GetValidationMessages(new FieldIdentifier(invoice, nameof(InvoiceModel.Paid)))];

    // What the generator emits for `public bool Paid { get; private set; }`: a bool? starting at the aggregate's
    // own default, and a rule over bool? that refuses only a null.
    private sealed class InvoiceModel
    {
        public bool? Paid { get; set; } = false;

        internal int Saves { get; set; }

        internal static Delegate? RuleOf(string property) => property == nameof(Paid) ? Rule : null;

        private static readonly Validate<bool?> Rule = static value => value is null ? [Required] : [];
    }
}
