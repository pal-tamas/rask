using Rask.Core;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's tab group: a tablist and the panels it switches between.
/// </summary>
/// <remarks>
///     What a tab group owes a reader: each tab tied to what it shows and back again, the panel of the
///     selected tab shown and reachable with Tab, the others in the markup but hidden — or, in a findable
///     group, hidden only until the browser's find-in-page lands in one.
/// </remarks>
public partial class UiTabGroupTests : global::Rask.Core.RaskMarkup
{
    private static Component Group(UiTabGroup group, UiTabs row) =>
        group[
            row[
                Ui.Tab.Name("profile")["Profile"],
                Ui.Tab.Name("account")["Account"]
            ],
            Ui.TabPanel.Name("profile")["Your profile."],
            Ui.TabPanel.Name("account")["Your account."]
        ];

    [Fact]
    public void Each_tab_and_its_panel_name_each_other()
    {
        var page = Page.Render(Group(Ui.TabGroup, Ui.Tabs));

        var tab = page.Find("[role=\"tab\"]:has-text(\"Account\")");
        var panel = page.Find("[role=\"tabpanel\"]:has-text(\"Your account.\")");

        Assert.False(string.IsNullOrEmpty(tab.Id));
        Assert.Equal(panel.Id, tab.Attribute("aria-controls"));
        Assert.Equal(tab.Id, panel.Attribute("aria-labelledby"));
    }

    [Fact]
    public void The_group_and_its_panels_are_marked_as_Flux_marks_them()
    {
        var page = Page.Render(Group(Ui.TabGroup, Ui.Tabs));

        var panels = page.FindAll("[data-ui-tab-group] > [data-ui-tab-panel][role=\"tabpanel\"]");

        Assert.Equal(2, panels.Count);
        Assert.Single(page.FindAll("[data-ui-tab-group] > [data-ui-tabs]"));
    }

    [Fact]
    public void Only_the_selected_tabs_panel_is_shown_and_it_is_a_tab_stop()
    {
        var page = Page.Render(Group(Ui.TabGroup, Ui.Tabs));

        var panels = page.FindAll("[role=\"tabpanel\"]");

        Assert.False(panels[0].Attributes.ContainsKey("hidden"));
        Assert.True(panels[0].Attributes.ContainsKey("data-selected"));
        Assert.Equal("0", panels[0].Attribute("tabindex"));
        Assert.True(panels[1].Attributes.ContainsKey("hidden"));
        Assert.False(panels[1].Attributes.ContainsKey("data-selected"));
        Assert.Equal("-1", panels[1].Attribute("tabindex"));
    }

    [Fact]
    public async Task Clicking_a_tab_shows_its_panel_and_hides_the_other()
    {
        var page = Page.Render(Group(Ui.TabGroup, Ui.Tabs));

        await page.On("[role=\"tab\"]:has-text(\"Account\")").Click();

        var panels = page.FindAll("[role=\"tabpanel\"]");
        Assert.True(panels[0].Attributes.ContainsKey("hidden"));
        Assert.False(panels[1].Attributes.ContainsKey("hidden"));
    }

    [Fact]
    public void The_rows_value_says_which_panel_is_shown()
    {
        var page = Page.Render(Group(Ui.TabGroup, Ui.Tabs.Value("account")));

        var shown = page.Find("[role=\"tabpanel\"][data-selected]");

        Assert.Equal("Your account.", shown.TextContent);
    }

    [Fact]
    public void A_findable_group_hides_a_panel_only_until_find_in_page_reaches_it()
    {
        var page = Page.Render(Group(Ui.TabGroup.Findable(), Ui.Tabs));

        var panels = page.FindAll("[role=\"tabpanel\"]");

        Assert.False(panels[0].Attributes.ContainsKey("hidden"));
        Assert.Equal("until-found", panels[1].Attribute("hidden"));
        Assert.True(panels[1].HasClass("absolute"));
    }

    [Fact]
    public async Task A_match_found_in_a_hidden_panel_selects_its_tab()
    {
        // `beforematch` is what the browser raises on an until-found element it is about to reveal.
        var page = Page.Render(Group(Ui.TabGroup.Findable(), Ui.Tabs));

        await page.On("[role=\"tabpanel\"][hidden]").Raise("beforematch");

        Assert.Equal("Account", page.Find("[role=\"tab\"][aria-selected=\"true\"]").TextContent);
        Assert.Equal("Your account.", page.Find("[role=\"tabpanel\"][data-selected]").TextContent);
    }

    [Fact]
    public void Two_groups_on_one_page_do_not_share_an_id()
    {
        var page = Page.Render(Div[Group(Ui.TabGroup, Ui.Tabs), Group(Ui.TabGroup, Ui.Tabs)]);

        var ids = page.FindAll("[id]").Select(node => node.Id).ToList();

        Assert.Equal(8, ids.Count);
        Assert.Equal(8, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_tab_in_a_row_with_no_group_controls_nothing()
    {
        var page = Page.Render(Ui.Tabs[Ui.Tab.Name("list")["List"]]);

        var tab = page.Find("[role=\"tab\"]");

        Assert.Null(tab.Attribute("aria-controls"));
        Assert.Null(tab.Id);
    }

    [Fact]
    public void A_panel_outside_a_group_is_its_own_content()
    {
        // Lifted out of a group during a refactor it shows what it holds rather than vanishing.
        var page = Page.Render(Ui.TabPanel.Name("orphan")["Orphan"]);

        var panel = page.Find("[role=\"tabpanel\"]");

        Assert.Equal("Orphan", panel.TextContent);
        Assert.False(panel.Attributes.ContainsKey("hidden"));
    }
}
