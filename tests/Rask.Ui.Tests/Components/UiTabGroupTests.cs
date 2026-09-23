using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     Tabs over panels in one page — the form for a view that has no URL of its own.
/// </summary>
/// <remarks>
///     <para>
///     The link form stays the default and is covered by <c>UiTabsTests</c>. What is pinned here is what a
///     tablist owes a reader: each tab tied to what it shows and back again, one tab stop rather than a walk
///     through every tab, the arrows moving between them, and the panels that are not shown still being IN the
///     markup so the browser's own find-in-page can reach them.
///     </para>
///     <para>
///     A <c>UiTabs</c> inside the group is not ceremony — a <c>tablist</c> may contain only tabs, so the panels
///     cannot be its siblings.
///     </para>
/// </remarks>
public partial class UiTabGroupTests : global::Rask.Core.RaskMarkup
{
    private static Component Group(string? selected = null, Callback<string>? onSelect = null)
    {
        var group = selected is null ? Ui.TabGroup : Ui.TabGroup.Selected(selected);
        if (onSelect is { } cb)
        {
            group = group.OnSelect(cb);
        }

        return group[
            Ui.Tabs[
                Ui.Tab.Key("d").Label("Details").Name("details"),
                Ui.Tab.Key("h").Label("History").Name("history")
            ],
            Ui.TabPanel.Key("pd").Name("details")["The details."],
            Ui.TabPanel.Key("ph").Name("history")["The history."]
        ];
    }

    [Fact]
    public void A_named_tab_inside_a_group_is_a_button_rather_than_a_link()
    {
        // There is nowhere for it to go — the panel is already on the page — and a link with href="#" is one
        // the browser will follow, putting a stray fragment in the address bar and breaking the back button it
        // was supposed to protect.
        var html = Group().ToHtml();

        Assert.Contains("<button", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"#\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tab_with_an_href_is_still_a_link()
    {
        // The default, and the one to reach for: a URL is bookmarkable and answers the back button.
        var html = Ui.Tabs[Ui.Tab.Label("All").Href("/orders")].ToHtml();

        Assert.Contains("href=\"/orders\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_tab_and_its_panel_name_each_other()
    {
        // aria-controls points at what the tab shows; aria-labelledby points back, so the panel is announced
        // with the words on the tab that opened it rather than as an unnamed region.
        var html = Group().ToHtml();

        Assert.Contains("aria-controls=", html, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=", html, StringComparison.Ordinal);
        Assert.Contains("role=\"tabpanel\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_selected_the_first_tab_is_the_one_shown()
    {
        // A group that opens with nothing shown is a set of panels with no way in, and a page should not have
        // to repeat its own first tab's name to avoid that.
        var html = Group().ToHtml();

        Assert.Contains("aria-selected=\"true\"", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "aria-selected=\"true\""));
    }

    [Fact]
    public void Only_the_selected_tab_is_a_tab_stop()
    {
        // The roving tabindex: Tab out of the tablist lands in the PANEL, where the reader is going, rather
        // than walking every remaining tab first.
        var html = Group().ToHtml();

        Assert.Equal(1, Occurrences(html, "tabindex=\"0\"") - Occurrences(html, "role=\"tabpanel\" tabindex=\"0\""));
        Assert.Contains("tabindex=\"-1\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_panel_is_rendered_and_the_hidden_ones_say_so()
    {
        // Rendered, not omitted: the content is then findable by the browser's own in-page search and by a
        // screen reader's virtual cursor, and switching tabs shows markup that is already there.
        var html = Group().ToHtml();

        Assert.Contains("The details.", html, StringComparison.Ordinal);
        Assert.Contains("The history.", html, StringComparison.Ordinal);

        // Counted on the PANEL tags, not on the whole document: "hidden" also appears in the tab row's
        // `[&::-webkit-scrollbar]:hidden`, so a bare Contains here would pass with no panel hidden at all.
        var panels = PanelTags(html);
        Assert.Equal(2, panels.Count);
        Assert.Equal(1, panels.Count(tag => tag.Contains("hidden", StringComparison.Ordinal)));
    }

    // The opening tag of each tabpanel, which is where `hidden` lands.
    private static List<string> PanelTags(string html)
    {
        var tags = new List<string>();
        for (var i = html.IndexOf("role=\"tabpanel\"", StringComparison.Ordinal); i >= 0;
             i = html.IndexOf("role=\"tabpanel\"", i + 1, StringComparison.Ordinal))
        {
            var open = html.LastIndexOf('<', i);
            var close = html.IndexOf('>', i);
            tags.Add(html[open..close]);
        }

        return tags;
    }

    [Fact]
    public void Selected_says_which_panel_is_shown()
    {
        // The panel tags are in document order, so the second is history's — and `hidden` lives on the TAG,
        // ahead of the content, which is why slicing from the words inside it would assert nothing.
        var panels = PanelTags(Group("history").ToHtml());

        Assert.Contains("hidden", panels[0], StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", panels[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clicking_a_tab_reports_the_name_it_showed()
    {
        string? heard = null;
        var page = global::Rask.Testing.RaskTest.Render(Group(onSelect: new Callback<string>(n => heard = n)));

        await page.On("[role=\"tab\"][aria-selected=\"false\"]").ClickAsync();

        Assert.Equal("history", heard);
    }

    [Fact]
    public async Task An_uncontrolled_group_keeps_track_itself()
    {
        // A page that does not care which tab is up should not have to hold a field for it.
        var page = global::Rask.Testing.RaskTest.Render(Group());

        await page.On("[role=\"tab\"][aria-selected=\"false\"]").ClickAsync();

        var panels = PanelTags(page.Html);
        Assert.Contains("hidden", panels[0], StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", panels[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_arrows_move_and_show_as_they_go()
    {
        // Automatic activation, which is the common tabs pattern and what Flux does. Home and End jump.
        string? heard = null;
        var page = global::Rask.Testing.RaskTest.Render(Group(onSelect: new Callback<string>(n => heard = n)));

        await page.On("[role=\"tablist\"]").RaiseAsync("keydown", "{\"key\":\"ArrowRight\"}");
        Assert.Equal("history", heard);

        await page.On("[role=\"tablist\"]").RaiseAsync("keydown", "{\"key\":\"Home\"}");
        Assert.Equal("details", heard);
    }

    [Fact]
    public async Task The_arrows_wrap_because_a_tab_row_is_a_ring()
    {
        string? heard = null;
        var page = global::Rask.Testing.RaskTest.Render(Group(onSelect: new Callback<string>(n => heard = n)));

        // Left from the first tab lands on the last, rather than stopping dead.
        await page.On("[role=\"tablist\"]").RaiseAsync("keydown", "{\"key\":\"ArrowLeft\"}");

        Assert.Equal("history", heard);
    }

    [Fact]
    public void A_panel_outside_a_group_is_its_own_content()
    {
        // Lifted out of a group during a refactor it should show what it holds, not vanish.
        Assert.Contains("Orphan", Ui.TabPanel.Name("x")["Orphan"].ToHtml(), StringComparison.Ordinal);
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
