using Rask.Core;
using Rask.Core.Forms;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     The kit's checkbox and switch over a generated form model's <c>bool?</c>, which carries the required rule over
///     <c>bool?</c> the generator writes for an aggregate's <c>bool</c>: they bind it as <c>bool</c>, and still ask it.
/// </summary>
public partial class UiNullableBoolFieldRuleTests : global::Rask.Core.RaskMarkup
{
    private const string Required = "This field is required.";

    static UiNullableBoolFieldRuleTests()
    {
        RaskValidation.RegisterFieldRules(typeof(InvoiceModel), InvoiceModel.RuleOf);
    }

    public static TheoryData<bool> Switches => [false, true];

    [Theory]
    [MemberData(nameof(Switches))]
    public async Task A_checkbox_or_switch_over_a_nullable_bool_with_its_rule_writes_true_then_false(bool isSwitch)
    {
        var invoice = new InvoiceModel();
        var (page, form) = Bound(invoice, isSwitch);

        await page.On("#paid").Change("true");
        var ticked = invoice.Paid;
        await page.On("#paid").Change("false");

        Assert.True(ticked);
        Assert.False(invoice.Paid);
        Assert.Empty(Messages(form, invoice));
    }

    [Theory]
    [MemberData(nameof(Switches))]
    public async Task An_unticked_checkbox_or_switch_over_a_nullable_bool_is_accepted_on_submit(bool isSwitch)
    {
        var invoice = new InvoiceModel();
        var (page, form) = Bound(invoice, isSwitch);

        await page.On("form").Submit();

        Assert.Equal(1, invoice.Saves);
        Assert.Empty(Messages(form, invoice));
        Assert.False(page.Exists("#paid[aria-invalid=\"true\"]"));
    }

    [Fact]
    public async Task A_checkbox_over_a_nullable_bool_that_holds_nothing_says_what_its_rule_says_on_submit()
    {
        var invoice = new InvoiceModel { Paid = null };
        var (page, form) = Bound(invoice, isSwitch: false);

        await page.On("form").Submit();

        Assert.Equal(0, invoice.Saves);
        Assert.Equal([Required], Messages(form, invoice));
    }

    private static (Page Page, EditContext Form) Bound(InvoiceModel invoice, bool isSwitch)
    {
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(invoice).OnSubmit(_ => invoice.Saves++)[
            isSwitch
                ? Ui.Switch.Bind(() => invoice.Paid).Label("Paid").Id("paid")
                : Ui.Checkbox.Bind(() => invoice.Paid).Label("Paid").Id("paid"),
            Rask.Testing.Test.EditContextProbe(c => form = c)
        ]);
        return (page, form!);
    }

    private static IReadOnlyList<string> Messages(EditContext form, InvoiceModel invoice) =>
        [.. form.GetValidationMessages(new FieldIdentifier(invoice, nameof(InvoiceModel.Paid)))];

    // What the generator emits for `public bool Paid { get; private set; }`: a bool? starting at the aggregate's
    // own default, and a rule over bool? that refuses only a null.
    private sealed class InvoiceModel
    {
        private static readonly Validate<bool?> Rule = static value => value is null ? [Required] : [];

        public bool? Paid { get; set; } = false;

        internal int Saves { get; set; }

        internal static Delegate? RuleOf(string property) => property == nameof(Paid) ? Rule : null;
    }
}
