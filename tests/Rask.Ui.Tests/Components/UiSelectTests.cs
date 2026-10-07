using Rask.Core;
using Rask.Core.Forms;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's select in its default variant — the browser's own <c>&lt;select&gt;</c> — and what every variant
///     shares: options as children, the typed binding, the field it draws around itself.
/// </summary>
/// <remarks>
///     The look is held to fluxui.dev by <c>SelectParity</c> and <c>scripts/flux/parity-select.mjs</c>; these
///     hold what a class string cannot say. The drawn variants are in <c>UiSelectListboxTests</c>,
///     <c>UiSelectSearchTests</c> and <c>UiSelectComboboxTests</c>.
/// </remarks>
public partial class UiSelectTests : global::Rask.Core.RaskMarkup
{
    private enum Plan
    {
        Free,
        Team,
    }

    private sealed class Profile
    {
        public string Country { get; set; } = "gb";

        public Plan? Plan { get; set; }

        public int Seats { get; set; } = 5;
    }

    private static Component[] Countries() =>
    [
        Ui.SelectOption.Value("hu")["Hungary"],
        Ui.SelectOption.Value("gb")["United Kingdom"],
    ];

    [Fact]
    public void The_default_variant_is_the_browsers_own_select()
    {
        var select = Ui.Select.Value("gb")[Countries()];

        var html = select.ToHtml();

        Assert.StartsWith("<select", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-select-native", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-control", html, StringComparison.Ordinal);
        Assert.DoesNotContain("popover", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_option_is_a_child_holding_a_value_and_its_words()
    {
        var select = Ui.Select.Value("gb")[Countries()];

        var html = select.ToHtml();

        Assert.Contains("value=\"hu\">Hungary</option>", html, StringComparison.Ordinal);
        Assert.Contains("selected value=\"gb\">United Kingdom</option>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_option_with_no_value_stands_for_its_own_words()
    {
        var select = Ui.Select.Value("Design services")[Ui.SelectOption["Photography"], Ui.SelectOption["Design services"]];

        var html = select.ToHtml();

        Assert.Contains("selected value=\"Design services\">Design services</option>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_placeholder_is_an_option_nobody_can_pick_and_the_chosen_one_until_there_is_an_answer()
    {
        var select = Ui.Select.Of<string>().Placeholder("Choose industry...")[Countries()];

        var html = select.ToHtml();

        Assert.Contains("<option disabled selected value=\"\">Choose industry...</option>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_is_an_optgroup_under_its_label()
    {
        var select = Ui.Select.Of<string>()[Ui.SelectGroup.Label("Europe")[Countries()]];

        var html = select.ToHtml();

        Assert.Contains("<optgroup label=\"Europe\"><option", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_option_is_shown_and_cannot_be_picked()
    {
        var select = Ui.Select.Value("gb")[Ui.SelectOption.Value("hu").Disabled()["Hungary"]];

        var option = Page.Render(select).Find("option");

        Assert.NotNull(option.Attribute("disabled"));
        Assert.Equal("hu", option.Attribute("value"));
    }

    [Fact]
    public async Task A_bound_select_marks_the_models_option_and_writes_a_pick_back()
    {
        var model = new Profile();
        var page = Page.Render(() => Form.Model(model)[Ui.Select.Bind(() => model.Country).Label("Country")[Countries()]]);
        var before = page.Html;

        await page.On("select").Change("hu");

        Assert.Contains("selected value=\"gb\"", before, StringComparison.Ordinal);
        Assert.Equal("hu", model.Country);
    }

    [Fact]
    public async Task A_bound_nullable_enum_reads_and_writes_its_own_type()
    {
        var model = new Profile();
        var page = Page.Render(() => Form.Model(model)[
            Ui.Select.Bind(() => model.Plan).Placeholder("Choose…")[
                Ui.SelectOption.Value(Plan.Free)["Free"],
                Ui.SelectOption.Value(Plan.Team)["Team"]
            ]
        ]);

        await page.On("select").Change("Team");

        Assert.Equal(Plan.Team, model.Plan);
    }

    [Fact]
    public void An_option_of_another_type_than_its_select_is_refused_by_name()
    {
        var model = new Profile();
        var select = Ui.Select.Bind(() => model.Seats)[Ui.SelectOption.Value("five")["Five"]];

        var thrown = Assert.Throws<InvalidOperationException>(() => select.ToHtml());

        Assert.Contains("\"Five\" holds String", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("Int32", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_draws_the_field_and_points_at_the_control()
    {
        var select = Ui.Select.Value("gb").Label("Country").Description("Where you are billed.").Badge("Optional")[Countries()];

        var page = Page.Render(select);

        Assert.True(page.Exists("[data-ui-field]"));
        Assert.Equal("f-country", page.Find("label").Attribute("for"));
        Assert.Contains("Optional", page.TextOf("label"), StringComparison.Ordinal);
        Assert.Equal("f-country-description", page.Find("select").Attribute("aria-describedby"));
        Assert.Equal("f-country", page.Find("select").Id);
    }

    [Fact]
    public void A_select_with_no_label_is_the_control_alone()
    {
        var select = Ui.Select.Value("gb")[Countries()];

        var html = select.ToHtml();

        Assert.DoesNotContain("data-ui-field", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<label", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_select_the_form_rejects_is_invalid_and_described_by_its_message()
    {
        var model = new Profile();
        var form = new EditContext(model);
        form.AddValidationMessage(new FieldIdentifier(model, nameof(Profile.Country)), "We do not ship there.");

        var html = Page.Render(() => Form.Model(model).Context(form)[
            Ui.Select.Bind(() => model.Country).Label("Country")[Countries()]
        ]).Html;

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-country-error\"", html, StringComparison.Ordinal);
        Assert.Contains("data-invalid", html, StringComparison.Ordinal);
        Assert.Contains("We do not ship there.", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.SelectSize.Base, "h-10")]
    [InlineData(Ui.SelectSize.Sm, "h-8")]
    [InlineData(Ui.SelectSize.Xs, "h-6")]
    public void The_size_is_the_inputs(Ui.SelectSize size, string height)
    {
        var select = Ui.Select.Value("gb").Size(size)[Countries()];

        var html = select.ToHtml();

        Assert.Contains(height + " ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_named_select_says_so_on_the_control()
    {
        var select = Ui.Select.Value("gb").Disabled().Name("country")[Countries()];

        var html = select.ToHtml();

        Assert.Contains(" disabled", html, StringComparison.Ordinal);
        Assert.Contains("name=\"country\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Picking_several_needs_a_select_that_holds_a_collection()
    {
        var select = Ui.Select.Value("gb").Listbox.Multiple()[Countries()];

        var thrown = Assert.Throws<InvalidOperationException>(() => select.ToHtml());

        Assert.Contains("holds a collection", thrown.Message, StringComparison.Ordinal);
    }
}
