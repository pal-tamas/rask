using Rask.Core;
using Rask.Core.Forms;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's radio group and its radios: what the group binds, what makes the keyboard work with no script,
///     and the markup each variant is drawn from.
/// </summary>
/// <remarks>The look is held to fluxui.dev by <c>RadioParity</c>; these hold what a class string cannot say.</remarks>
public partial class UiRadioTests : global::Rask.Core.RaskMarkup
{
    private enum Plan
    {
        Free,
        Pro,
        Team,
    }

    private sealed class Account
    {
        public Plan Plan { get; set; }

        public string? Shipping { get; set; }
    }

    [Fact]
    public void A_group_binds_one_value_and_chooses_the_radio_that_holds_it()
    {
        var model = new Account { Plan = Plan.Pro };

        var html = Page.Render(() => Plans(model)).Html;

        Assert.Contains("role=\"radiogroup\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"f-plan-label\"", html, StringComparison.Ordinal);
        Assert.Equal(3, Occurrences(html, "type=\"radio\""));
        Assert.Equal(1, Occurrences(html, " checked"));
        Assert.Contains("value=\"Pro\" checked", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Choosing_a_radio_writes_its_value_to_the_bound_member()
    {
        var model = new Account();
        var page = Page.Render(() => Plans(model));

        await page.On("#f-plan-team").Change("Team");

        Assert.Equal(Plan.Team, model.Plan);
    }

    [Fact]
    public async Task A_controlled_group_reports_the_chosen_value()
    {
        string? seen = null;
        var page = Page.Render(() =>
            Ui.RadioGroup.Value("standard").Id("shipping").OnChange(next => seen = next)[
                Ui.Radio.Value("standard").Label("Standard"),
                Ui.Radio.Value("fast").Label("Fast")
            ]);

        await page.On("#shipping-fast").Change("fast");

        Assert.Equal("fast", seen);
    }

    [Fact]
    public void Every_radio_shares_the_groups_name_so_the_arrow_keys_and_the_form_post_are_the_browsers()
    {
        var model = new Account();

        var html = Page.Render(() => Plans(model)).Html;

        Assert.Equal(3, Occurrences(html, "name=\"f-plan\""));
    }

    [Fact]
    public void A_name_forwarded_to_a_radio_is_the_one_it_posts_under()
    {
        var html = Page.Render(() => Ui.RadioGroup.Value("editor").Label("Role")[
            Ui.Radio.Value("editor").Label("Editor").Attributes(("name", "role"))
        ]).Html;

        Assert.Equal(1, Occurrences(html, "name=\"role\""));
        Assert.DoesNotContain("name=\"f-role\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_radio_with_a_label_is_an_inline_field_its_label_naming_the_input()
    {
        var model = new Account();

        var html = Page.Render(() => Plans(model)).Html;

        Assert.Contains("id=\"f-plan-free\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"f-plan-free\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-plan-free-description\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Checked_chooses_a_radio_only_while_the_group_holds_no_value()
    {
        var unset = Page.Render(() => Shipping(null)).Html;
        var set = Page.Render(() => Shipping("standard")).Html;

        Assert.Contains("value=\"fast\" checked", unset, StringComparison.Ordinal);
        Assert.Contains("value=\"standard\" checked", set, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(set, " checked"));
    }

    [Fact]
    public void A_disabled_radio_disables_its_input_and_no_other()
    {
        var html = Page.Render(() =>
            Ui.RadioGroup.Value("standard").Label("Shipping")[
                Ui.Radio.Value("standard").Label("Standard"),
                Ui.Radio.Value("fast").Label("Fast").Disabled()
            ]).Html;

        Assert.Equal(1, Occurrences(html, " disabled"));
    }

    [Theory]
    [InlineData(Ui.RadioGroupVariant.Segmented, "data-ui-radio-group-segmented", "data-ui-radio-segmented")]
    [InlineData(Ui.RadioGroupVariant.Cards, "data-ui-radio-group-cards", "data-ui-radio-cards")]
    [InlineData(Ui.RadioGroupVariant.Pills, "data-ui-radio-group-pills", "data-ui-radio-pills")]
    [InlineData(Ui.RadioGroupVariant.Buttons, "data-ui-radio-group-buttons", "data-ui-radio-buttons")]
    public void Every_variant_keeps_a_real_input_inside_the_label_it_draws(
        Ui.RadioGroupVariant variant, string group, string radio)
    {
        var html = Page.Render(() =>
            Ui.RadioGroup.Value("fast").Label("Shipping").Variant(variant)[
                Ui.Radio.Value("standard").Label("Standard"),
                Ui.Radio.Value("fast").Label("Fast")
            ]).Html;

        Assert.Contains(group, html, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(html, radio));
        Assert.Equal(2, Occurrences(html, "type=\"radio\""));
        Assert.Contains("value=\"fast\" checked", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.RadioGroupVariant.Segmented)]
    [InlineData(Ui.RadioGroupVariant.Cards)]
    [InlineData(Ui.RadioGroupVariant.Buttons)]
    public void A_segment_a_card_and_a_button_draw_the_radios_icon(Ui.RadioGroupVariant variant)
    {
        var html = Page.Render(() =>
            Ui.RadioGroup.Value("fast").Label("Shipping").Variant(variant)[
                Ui.Radio.Value("standard").Label("Standard").Icon(Ui.IconName.Truck),
                Ui.Radio.Value("fast").Label("Fast").Icon(Ui.IconName.Cube)
            ]).Html;

        // The chosen card's dot is no icon: only the two the radios were given.
        Assert.Equal(2, Occurrences(html, "data-slot=\"icon\""));
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(false, 0)]
    public void Cards_draw_their_dot_unless_the_group_says_not_to(bool? indicator, int dots)
    {
        var html = Page.Render(() =>
            Ui.RadioGroup.Value("fast").Label("Shipping").Cards.Indicator(indicator)[
                Ui.Radio.Value("standard").Label("Standard").Description("4-10 business days"),
                Ui.Radio.Value("fast").Label("Fast").Description("2-5 business days")
            ]).Html;

        Assert.Equal(dots, Occurrences(html, "data-ui-radio-indicator=\"\""));
    }

    [Fact]
    public void A_cards_children_replace_its_label_and_place_the_indicator_themselves()
    {
        var html = Page.Render(() =>
            Ui.RadioGroup.Value("fast").Label("Shipping").Cards[
                Ui.Radio.Value("fast").Label("Hidden by the children")[Ui.RadioIndicator, Span["Fast, your way"]]
            ]).Html;

        Assert.Contains("Fast, your way", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden by the children", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "data-ui-radio-indicator=\"\""));
    }

    [Fact]
    public void A_bound_group_the_form_rejects_is_invalid_and_described_by_its_message()
    {
        var model = new Account();
        var form = new EditContext(model);
        form.AddValidationMessage(new FieldIdentifier(model, nameof(Account.Plan)), "Pick a paid plan.");

        var html = Page.Render(() => Form.Model(model).Context(form)[Plans(model)]).Html;

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-plan-error\"", html, StringComparison.Ordinal);
        Assert.Contains("Pick a paid plan.", html, StringComparison.Ordinal);
        Assert.Equal(3, Occurrences(html, "data-invalid=\"\""));
    }

    private static Component Plans(Account model) =>
        Ui.RadioGroup.Bind(() => model.Plan).Label("Plan")[
            Ui.Radio.Value(Plan.Free).Label("Free").Description("For one person."),
            Ui.Radio.Value(Plan.Pro).Label("Pro"),
            Ui.Radio.Value(Plan.Team).Label("Team")
        ];

    private static Component Shipping(string? value) =>
        Ui.RadioGroup.Value(value).Label("Shipping")[
            Ui.Radio.Value("standard").Label("Standard"),
            Ui.Radio.Value("fast").Label("Fast").Checked()
        ];

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
