using Rask.Core;
using Rask.Core.Forms;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's checkbox, its group and its check-all: what they bind, what they tell assistive tech, and the
///     markup each variant is drawn from.
/// </summary>
/// <remarks>The look is held to fluxui.dev by <c>CheckboxParity</c>; these hold what a class string cannot say.</remarks>
public partial class UiCheckboxTests : global::Rask.Core.RaskMarkup
{
    private sealed class Account
    {
        public bool Agreed { get; set; }

        public bool? Newsletter { get; set; }

        public List<string> Topics { get; set; } = [];

        public HashSet<int> Days { get; set; } = [];
    }

    [Fact]
    public void A_checkbox_is_a_label_around_a_real_checkbox_input()
    {
        var html = Ui.Checkbox.ToHtml();

        Assert.StartsWith("<label", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-checkbox=\"\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-checkbox-indicator=\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_draws_an_inline_field_with_the_box_before_its_words()
    {
        var html = Ui.Checkbox.Label("Remember me").Description("On this device only.").ToHtml();

        Assert.StartsWith("<div", html, StringComparison.Ordinal);
        Assert.Contains("for=\"f-remember-me\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-remember-me-description\"", html, StringComparison.Ordinal);
        Assert.True(
            html.IndexOf("data-ui-checkbox=\"\"", StringComparison.Ordinal) < html.IndexOf("data-ui-label=\"\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Checked_is_the_state_of_a_checkbox_nothing_is_bound_to(bool on)
    {
        var html = Ui.Checkbox.Checked(on).Label("Remember me").ToHtml();

        Assert.Equal(on, html.Contains(" checked", StringComparison.Ordinal));
        Assert.Equal(on, html.Contains("data-checked=\"\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ticking_a_controlled_checkbox_reports_the_new_state()
    {
        bool? seen = null;
        var page = Page.Render(() => Ui.Checkbox.Id("remember").Checked(false).OnChange(on => seen = on));

        await page.On("#remember").Change("true");

        Assert.True(seen);
    }

    [Fact]
    public async Task Ticking_a_bound_checkbox_writes_the_model()
    {
        var model = new Account();
        var page = Page.Render(() => Form.Model(model)[Ui.Checkbox.Bind(() => model.Agreed).Label("I agree")]);

        await page.On("#f-agreed").Change("true");

        Assert.True(model.Agreed);
    }

    [Fact]
    public async Task A_nullable_member_is_bound_and_drawn_with_a_dash_while_it_is_null()
    {
        var model = new Account();
        var page = Page.Render(() => Form.Model(model)[Ui.Checkbox.Bind(() => model.Newsletter).Label("Newsletter")]);
        var unanswered = page.Html;

        await page.On("#f-newsletter").Change("true");

        Assert.Contains("data-indeterminate=\"\"", unanswered, StringComparison.Ordinal);
        Assert.True(model.Newsletter);
        Assert.DoesNotContain("data-indeterminate=\"\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_indeterminate_checkbox_is_marked_as_Flux_marks_it_and_is_not_checked()
    {
        var html = Ui.Checkbox.Checked().Indeterminate().ToHtml();

        Assert.Contains("data-indeterminate=\"\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-checked", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" checked", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_checkbox_disables_the_input_itself()
    {
        var html = Ui.Checkbox.Label("Read and write").Disabled().ToHtml();

        Assert.Contains(" disabled", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_checkbox_says_so_to_assistive_tech_and_to_the_stylesheet()
    {
        var html = Ui.Checkbox.Label("Terms").Invalid().ToHtml();

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-invalid=\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_checkbox_the_form_rejects_is_invalid_and_described_by_its_message()
    {
        var model = new Account();
        var form = new EditContext(model);
        form.AddValidationMessage(new FieldIdentifier(model, nameof(Account.Agreed)), "You must agree.");

        var html = Page.Render(() => Form.Model(model).Context(form)[Ui.Checkbox.Bind(() => model.Agreed).Label("I agree")]).Html;

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-agreed-error\"", html, StringComparison.Ordinal);
        Assert.Contains("You must agree.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_is_what_a_plain_form_posts_and_none_leaves_the_browsers_own()
    {
        var valued = Ui.Checkbox.Value("push").Name("notify").ToHtml();
        var bare = Ui.Checkbox.ToHtml();

        Assert.Contains("name=\"notify\"", valued, StringComparison.Ordinal);
        Assert.Contains("value=\"push\"", valued, StringComparison.Ordinal);
        Assert.DoesNotContain("value=", bare, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_binds_the_collection_and_ticks_the_checkboxes_whose_values_it_holds()
    {
        var model = new Account { Topics = ["news", "jobs"] };

        var html = Page.Render(() => Topics(model)).Html;

        Assert.Contains("role=\"group\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"f-topics-label\"", html, StringComparison.Ordinal);
        Assert.Equal(3, Occurrences(html, "type=\"checkbox\""));
        Assert.Equal(2, Occurrences(html, " checked"));
    }

    [Fact]
    public async Task Ticking_a_checkbox_of_a_group_adds_its_value_in_the_order_the_choices_are_written()
    {
        var model = new Account { Topics = ["jobs"] };
        var page = Page.Render(() => Topics(model));

        await page.On("#f-topics-news").Change("true");

        Assert.Equal(["news", "jobs"], model.Topics);
    }

    [Fact]
    public async Task Clearing_a_checkbox_of_a_group_takes_its_value_out()
    {
        var model = new Account { Topics = ["news", "jobs"] };
        var page = Page.Render(() => Topics(model));

        await page.On("#f-topics-news").Change("false");

        Assert.Equal(["jobs"], model.Topics);
    }

    [Fact]
    public async Task A_group_binds_a_set_of_values_of_any_type()
    {
        var model = new Account();
        var page = Page.Render(() =>
            Ui.CheckboxGroup.Bind(() => model.Days).Label("Days")[
                Ui.Checkbox.Value(1).Label("Monday"),
                Ui.Checkbox.Value(2).Label("Tuesday")
            ]);

        await page.On("#f-days-2").Change("true");

        Assert.Equal([2], model.Days);
    }

    [Fact]
    public async Task A_controlled_group_reports_the_whole_new_selection()
    {
        ICollection<string>? seen = null;
        var page = Page.Render(() =>
            Ui.CheckboxGroup.Values(["news"]).Id("topics").OnChange(next => seen = next)[
                Ui.Checkbox.Value("news").Label("News"),
                Ui.Checkbox.Value("jobs").Label("Jobs")
            ]);

        await page.On("#topics-jobs").Change("true");

        Assert.Equal(["news", "jobs"], seen);
    }

    [Theory]
    [InlineData(Ui.CheckboxGroupVariant.Cards, "data-ui-checkbox-group-cards", "data-ui-checkbox-cards")]
    [InlineData(Ui.CheckboxGroupVariant.Pills, "data-ui-checkbox-group-pills", "data-ui-checkbox-pills")]
    [InlineData(Ui.CheckboxGroupVariant.Buttons, "data-ui-checkbox-group-buttons", "data-ui-checkbox-buttons")]
    public void Every_variant_keeps_a_real_input_inside_the_label_it_draws(
        Ui.CheckboxGroupVariant variant, string group, string checkbox)
    {
        var html = Page.Render(() =>
            Ui.CheckboxGroup.Values(["news"]).Label("Topics").Variant(variant)[
                Ui.Checkbox.Value("news").Label("News").Description("Once a month."),
                Ui.Checkbox.Value("jobs").Label("Jobs")
            ]).Html;

        Assert.Contains(group, html, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(html, checkbox));
        Assert.Equal(2, Occurrences(html, "type=\"checkbox\""));
        Assert.Equal(1, Occurrences(html, " checked"));
    }

    [Fact]
    public void A_card_draws_its_label_description_and_indicator_and_its_children_replace_them()
    {
        var html = Page.Render(() =>
            Ui.CheckboxGroup.Values(["news"]).Label("Topics").Variant(Ui.CheckboxGroupVariant.Cards)[
                Ui.Checkbox.Value("news").Label("News").Description("Once a month."),
                Ui.Checkbox.Value("jobs").Label("Hidden by the children")[Ui.CheckboxIndicator, Span["Jobs, your way"]]
            ]).Html;

        Assert.Contains("Once a month.", html, StringComparison.Ordinal);
        Assert.Contains("Jobs, your way", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden by the children", html, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(html, "data-ui-checkbox-indicator=\"\""));
    }

    [Fact]
    public void A_disabled_group_disables_every_checkbox_in_it()
    {
        var html = Page.Render(() =>
            Ui.CheckboxGroup.Values(["news"]).Label("Topics").Disabled()[
                Ui.Checkbox.Value("news").Label("News"),
                Ui.Checkbox.Value("jobs").Label("Jobs")
            ]).Html;

        Assert.Equal(2, Occurrences(html, " disabled"));
    }

    [Theory]
    [InlineData(new string[0], false, false)]
    [InlineData(new[] { "news" }, false, true)]
    [InlineData(new[] { "news", "jobs" }, true, false)]
    public void Check_all_is_clear_for_none_a_dash_for_some_and_ticked_for_all(string[] picked, bool all, bool some)
    {
        var html = Page.Render(() => WithCheckAll(picked, _ => { })).Html;
        var box = html[..html.IndexOf("topics-news", StringComparison.Ordinal)];

        Assert.Equal(all, box.Contains(" checked", StringComparison.Ordinal));
        Assert.Equal(some, box.Contains("data-indeterminate=\"\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new string[0], new[] { "news", "jobs" })]
    [InlineData(new[] { "news" }, new[] { "news", "jobs" })]
    [InlineData(new[] { "news", "jobs" }, new string[0])]
    public async Task Pressing_check_all_ticks_every_checkbox_unless_they_all_are(string[] picked, string[] expected)
    {
        ICollection<string>? seen = null;
        var page = Page.Render(() => WithCheckAll(picked, next => seen = next));

        await page.On("#topics-all").Change("true");

        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task Check_all_leaves_a_disabled_checkbox_as_it_is()
    {
        ICollection<string>? seen = null;
        var page = Page.Render(() =>
            Ui.CheckboxGroup.Values(["jobs"]).Id("topics").OnChange(next => seen = next)[
                Ui.CheckboxAll.Label("Everything"),
                Ui.Checkbox.Value("news").Label("News"),
                Ui.Checkbox.Value("jobs").Label("Jobs").Disabled(),
                Ui.Checkbox.Value("events").Label("Events").Disabled()
            ]);

        await page.On("#topics-all").Change("true");

        Assert.Equal(["news", "jobs"], seen);
    }

    private static Component Topics(Account model) =>
        Form.Model(model)[
            Ui.CheckboxGroup.Bind(() => model.Topics).Label("Topics")[
                Ui.Checkbox.Value("news").Label("News"),
                Ui.Checkbox.Value("releases").Label("Releases"),
                Ui.Checkbox.Value("jobs").Label("Jobs")
            ]
        ];

    // The check-all sits anywhere in the group's markup: here, a row above the choices.
    private static Component WithCheckAll(string[] picked, Action<ICollection<string>> changed) =>
        Ui.CheckboxGroup.Values(picked).Id("topics").OnChange(changed)[
            Div[Ui.CheckboxAll.Label("Everything")],
            Ui.Checkbox.Value("news").Label("News"),
            Ui.Checkbox.Value("jobs").Label("Jobs")
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
