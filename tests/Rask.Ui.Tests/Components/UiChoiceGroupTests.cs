namespace Rask.UiTests.Components;

/// <summary>
///     A list of choices as ONE field — radios for one answer, checkboxes for several.
/// </summary>
/// <remarks>
///     <para>
///     The thing worth pinning is that every layout keeps a real <c>&lt;input&gt;</c>. A card, a pill and a
///     segment look like buttons, and a button is the one thing a choice must not be: the browser's own
///     grouping, the arrow keys inside a radio group, the space bar, the form post and every assistive
///     technology all come from the input being there. The look is <c>has-[:checked]:</c> rules on the label
///     around it — CSS reading the input's own state, with nothing to keep in sync.
///     </para>
///     <para>
///     The other half is the binding: one field, not one per option, which is what <c>UiRadio</c> (bound to its
///     own bool) could never give a form.
///     </para>
/// </remarks>
public partial class UiChoiceGroupTests : global::Rask.Core.RaskMarkup
{
    private static readonly (string Value, string Text)[] Plans =
        [("free", "Free"), ("pro", "Pro"), ("team", "Team")];

    private sealed class Account
    {
        public string Plan { get; set; } = "free";

        public List<string> Topics { get; set; } = ["news"];
    }

    [Fact]
    public void A_radio_group_binds_the_groups_value_rather_than_one_option()
    {
        var model = new Account { Plan = "pro" };
        var html = Ui.RadioGroup.Bind(() => model.Plan).Options(Plans).Label("Plan").ToHtml();

        Assert.Contains("role=\"radiogroup\"", html, StringComparison.Ordinal);
        Assert.Equal(3, Occurrences(html, "type=\"radio\""));
        Assert.Equal(1, Occurrences(html, "checked"));
    }

    [Fact]
    public void Every_radio_shares_one_name_so_the_browser_makes_them_exclusive()
    {
        // Exclusivity is the browser's, not this component's. Without a shared name every option would be
        // independently checkable and the group would silently stop being a group.
        var html = Ui.RadioGroup.Value("free").Options(Plans).Label("Plan").Name("plan").ToHtml();

        Assert.Equal(3, Occurrences(html, "name=\"plan\""));
    }

    [Theory]
    [InlineData(Ui.ChoiceLayout.List)]
    [InlineData(Ui.ChoiceLayout.Cards)]
    [InlineData(Ui.ChoiceLayout.Pills)]
    [InlineData(Ui.ChoiceLayout.Buttons)]
    [InlineData(Ui.ChoiceLayout.Segmented)]
    public void Every_layout_keeps_a_real_input(Ui.ChoiceLayout layout)
    {
        // The whole point. A layout that swapped the input for a <button> would look identical and lose the
        // keyboard, the grouping and the form post — and nothing would report it.
        var html = Ui.RadioGroup.Value("free").Options(Plans).Label("Plan").Layout(layout).ToHtml();

        Assert.Equal(3, Occurrences(html, "type=\"radio\""));
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_layouts_that_are_all_affordance_hide_the_box_without_hiding_it_from_a_screen_reader()
    {
        // sr-only, never `hidden` or `display: none`: those take the input out of the accessibility tree and
        // the tab order too, which would leave a pill nobody can reach by keyboard.
        var html = Ui.RadioGroup.Value("free").Options(Plans).Label("Plan").Layout(Ui.ChoiceLayout.Pills).ToHtml();

        Assert.Contains("sr-only", html, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_description_is_drawn_where_there_is_room_for_it()
    {
        Assert.Contains("Everything, billed monthly",
            Ui.RadioGroup.Value("free").Options(Plans).Label("Plan").Layout(Ui.ChoiceLayout.Cards)
                .OptionDescription(v => v == "pro" ? "Everything, billed monthly" : null).ToHtml(),
            StringComparison.Ordinal);

        // A list row has one line; a description there would be a second line the layout never designed for.
        Assert.DoesNotContain("Everything, billed monthly",
            Ui.RadioGroup.Value("free").Options(Plans).Label("Plan")
                .OptionDescription(v => v == "pro" ? "Everything, billed monthly" : null).ToHtml(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Picking_a_radio_writes_the_groups_value()
    {
        var model = new Account();
        var page = global::Rask.Testing.Page.Render(
            Ui.RadioGroup.Bind(() => model.Plan).Options(Plans).Label("Plan"));

        await page.On("#f-plan-0").ChangeAsync("true");

        Assert.Equal("free", model.Plan);
    }

    [Fact]
    public void A_checkbox_group_binds_the_collection_the_model_declares()
    {
        var model = new Account { Topics = ["news", "releases"] };
        var html = Ui.CheckboxGroup.Bind(() => model.Topics)
            .Options([("news", "News"), ("releases", "Releases"), ("jobs", "Jobs")])
            .Label("Topics").ToHtml();

        Assert.Contains("role=\"group\"", html, StringComparison.Ordinal);
        Assert.Equal(3, Occurrences(html, "type=\"checkbox\""));
        Assert.Equal(2, Occurrences(html, "checked"));
    }

    [Fact]
    public void Check_all_says_mixed_rather_than_claiming_all_over_a_half_filled_list()
    {
        // "All" checked over two of three is a lie somebody acts on. aria-checked="mixed" is the only way to
        // say it, since there is no `indeterminate` attribute — only the DOM property.
        var html = Ui.CheckboxGroup.Values<string>(["news"])
            .Options([("news", "News"), ("releases", "Releases")])
            .Label("Topics").CheckAll(true).ToHtml();

        Assert.Contains("aria-checked=\"mixed\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_all_becomes_clear_all_once_everything_is_in()
    {
        var html = Ui.CheckboxGroup.Values<string>(["news", "releases"])
            .Options([("news", "News"), ("releases", "Releases")])
            .Label("Topics").CheckAll(true).ToHtml();

        Assert.Contains("Clear all", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Select all<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_all_ignores_the_choices_it_may_not_pick()
    {
        // Everything selectable is already in, so the bulk action has nothing left to add even though one
        // choice is unpicked — it must not offer to pick a disabled row.
        var html = Ui.CheckboxGroup.Values<string>(["news"])
            .Options([("news", "News"), ("releases", "Releases")])
            .Label("Topics").CheckAll(true).OptionDisabled(v => v == "releases").ToHtml();

        Assert.Contains("Clear all", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Picking_a_checkbox_adds_to_the_models_own_collection()
    {
        var model = new Account { Topics = [] };
        var page = global::Rask.Testing.Page.Render(
            Ui.CheckboxGroup.Bind(() => model.Topics)
                .Options([("news", "News"), ("releases", "Releases")])
                .Label("Topics"));

        await page.On("#f-topics-0").ChangeAsync("true");

        Assert.Equal(["news"], model.Topics);
    }

    [Fact]
    public void The_group_is_named_by_its_own_label_rather_than_leaving_the_choices_unattached()
    {
        // Each choice has its own words; the GROUP's question — "Plan" — has nowhere else to live, and a
        // reader who arrives at "Free / Pro / Team" with no heading has to guess what is being asked.
        var html = Ui.RadioGroup.Value("free").Options(Plans).Label("Plan").ToHtml();

        Assert.Contains("aria-labelledby=", html, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
