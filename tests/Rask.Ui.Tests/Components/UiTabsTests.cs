using Rask.Core;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's tabs row: which tab is selected, who holds that, and the markup each variant writes.
/// </summary>
/// <remarks>
///     <para>
///     What is pinned is the contract the runtime's tablist keyboard and a screen reader both read:
///     <c>role="tablist"</c> over <c>button role="tab"</c>, one tab stop, <c>aria-selected</c> on every tab,
///     <c>disabled</c> on a tab the arrows pass over. Which tab an arrow lands on is the runtime's own rule,
///     tested where it lives (<c>TablistKeyboardTests</c> in Rask.Core.Tests); that it is then focused and
///     pressed is the site's E2E.
///     </para>
///     <para>
///     Pixels are <c>TabsParity</c>'s; the class assertions here only say which of a variant's drawings was
///     chosen.
///     </para>
/// </remarks>
public partial class UiTabsTests : global::Rask.Core.RaskMarkup
{
    private sealed class Settings
    {
        public string Tab { get; set; } = "account";
    }

    private static Component Three(UiTabs row) =>
        row[
            Ui.Tab.Name("profile")["Profile"],
            Ui.Tab.Name("account")["Account"],
            Ui.Tab.Name("billing")["Billing"]
        ];

    private static string? Selected(Page page) =>
        page.Find("[role=\"tab\"][aria-selected=\"true\"]").TextContent;

    [Fact]
    public void The_row_is_a_tablist_of_buttons_marked_as_Flux_marks_them()
    {
        var page = Page.Render(Three(Ui.Tabs));

        var tabs = page.FindAll("[data-ui-tabs][role=\"tablist\"] > button[data-ui-tab][role=\"tab\"]");

        Assert.Equal(3, tabs.Count);
        Assert.All(tabs, tab => Assert.Equal("button", tab.Attribute("type")));
    }

    [Fact]
    public void The_first_tab_is_selected_when_nothing_says_which()
    {
        var page = Page.Render(Three(Ui.Tabs));

        var tabs = page.FindAll("[role=\"tab\"]");

        Assert.Equal(["true", "false", "false"], tabs.Select(tab => tab.Attribute("aria-selected")));
        Assert.True(tabs[0].Attributes.ContainsKey("data-selected"));
        Assert.False(tabs[1].Attributes.ContainsKey("data-selected"));
    }

    [Fact]
    public void Only_the_selected_tab_is_a_tab_stop()
    {
        var page = Page.Render(Three(Ui.Tabs.Value("account")));

        var stops = page.FindAll("[role=\"tab\"]").Select(tab => tab.Attribute("tabindex"));

        Assert.Equal(["-1", "0", "-1"], stops);
    }

    [Fact]
    public void A_tab_that_says_selected_is_selected_rather_than_the_first()
    {
        var row = Ui.Tabs[
            Ui.Tab.Name("profile")["Profile"],
            Ui.Tab.Name("account").Selected()["Account"]
        ];

        var page = Page.Render(row);

        Assert.Equal("Account", Selected(page));
        Assert.Single(page.FindAll("[aria-selected=\"true\"]"));
    }

    [Fact]
    public async Task Clicking_a_tab_selects_it_and_reports_its_name()
    {
        string? heard = null;
        var page = Page.Render(Three(Ui.Tabs.OnChange(name => heard = name)));

        await page.On("[role=\"tab\"]:has-text(\"Billing\")").Click();

        Assert.Equal("billing", heard);
        Assert.Equal("Billing", Selected(page));
        Assert.Equal("0", page.Find("[role=\"tab\"]:has-text(\"Billing\")").Attribute("tabindex"));
    }

    [Fact]
    public async Task A_row_with_no_handler_at_all_keeps_track_itself()
    {
        var page = Page.Render(Three(Ui.Tabs));

        await page.On("[role=\"tab\"]:has-text(\"Account\")").Click();

        Assert.Equal("Account", Selected(page));
    }

    [Fact]
    public async Task A_row_given_a_value_stays_on_it_until_the_page_changes_it()
    {
        string? asked = null;
        var page = Page.Render(Three(Ui.Tabs.Value("profile").OnChange(name => asked = name)));

        await page.On("[role=\"tab\"]:has-text(\"Billing\")").Click();

        Assert.Equal("billing", asked);
        Assert.Equal("Profile", Selected(page));
    }

    [Fact]
    public async Task A_bound_row_reads_the_field_and_writes_the_selected_name_back()
    {
        var settings = new Settings();
        var page = Page.Render(Three(Ui.Tabs.Bind(() => settings.Tab)));
        var before = Selected(page);

        await page.On("[role=\"tab\"]:has-text(\"Billing\")").Click();

        Assert.Equal("Account", before);
        Assert.Equal("billing", settings.Tab);
        Assert.Equal("Billing", Selected(page));
    }

    [Fact]
    public async Task A_tab_without_a_name_is_known_by_its_place_in_the_row()
    {
        string? heard = null;
        var row = Ui.Tabs.Segmented.OnChange(name => heard = name)[Ui.Tab["List"], Ui.Tab["Board"], Ui.Tab["Timeline"]];
        var page = Page.Render(row);

        await page.On("[role=\"tab\"]:has-text(\"Timeline\")").Click();

        Assert.Equal("2", heard);
        Assert.Equal("Timeline", Selected(page));
    }

    [Fact]
    public void A_disabled_tab_is_a_disabled_button_that_is_never_the_one_selected_by_default()
    {
        // `disabled` is what the runtime's arrow keys and the pointer both pass over.
        var row = Ui.Tabs[
            Ui.Tab.Name("profile").Disabled()["Profile"],
            Ui.Tab.Name("account")["Account"]
        ];

        var page = Page.Render(row);

        var off = page.Find("[role=\"tab\"]:has-text(\"Profile\")");
        Assert.True(off.Attributes.ContainsKey("disabled"));
        Assert.Equal("-1", off.Attribute("tabindex"));
        Assert.Equal("Account", Selected(page));
    }

    [Fact]
    public async Task An_action_is_a_button_in_the_row_that_is_not_a_tab_and_selects_nothing()
    {
        var added = 0;
        var row = Ui.Tabs[
            Ui.Tab.Name("tab-1")["Tab #1"],
            Ui.Tab.Icon(Ui.IconName.Plus).Action().OnClick(() => added++)["Add tab"]
        ];
        var page = Page.Render(row);

        await page.On("button:has-text(\"Add tab\")").Click();

        var action = page.Find("button:has-text(\"Add tab\")");
        Assert.Equal(1, added);
        Assert.Null(action.Attribute("role"));
        Assert.Null(action.Attribute("tabindex"));
        Assert.True(action.Attributes.ContainsKey("data-ui-tab"));
        Assert.Equal("Tab #1", Selected(page));
    }

    [Theory]
    [InlineData(Ui.TabsVariant.Default, "border-b", "border-fx-accent-content", "border-transparent")]
    [InlineData(Ui.TabsVariant.Segmented, "rounded-lg", "shadow-xs", "text-zinc-600")]
    [InlineData(Ui.TabsVariant.Pills, "h-8", "bg-fx-accent", "bg-zinc-800/5")]
    public void Each_variant_draws_its_row_its_selected_tab_and_the_others(
        Ui.TabsVariant variant, string row, string selected, string other)
    {
        var page = Page.Render(Three(Ui.Tabs.Variant(variant)));

        var tabs = page.FindAll("[role=\"tab\"]");

        Assert.True(page.Find("[role=\"tablist\"]").HasClass(row));
        Assert.True(tabs[0].HasClass(selected));
        Assert.False(tabs[1].HasClass(selected));
        Assert.True(tabs[1].HasClass(other));
    }

    [Theory]
    [InlineData(Ui.TabsVariant.Default, "border-zinc-800", "border-fx-accent-content")]
    [InlineData(Ui.TabsVariant.Pills, "bg-zinc-800", "bg-fx-accent")]
    public void A_tab_that_turns_the_accent_off_is_selected_in_the_base_colour(
        Ui.TabsVariant variant, string plain, string accent)
    {
        var row = Ui.Tabs.Variant(variant)[Ui.Tab.Accent(false)["List"], Ui.Tab["Board"]];

        var tab = Page.Render(row).Find("[aria-selected=\"true\"]");

        Assert.True(tab.HasClass(plain));
        Assert.False(tab.HasClass(accent));
    }

    [Fact]
    public void A_small_segmented_row_is_shorter_and_its_tabs_narrower()
    {
        var page = Page.Render(Ui.Tabs.Segmented.Size(Ui.TabsSize.Sm)[Ui.Tab["Demo"], Ui.Tab["Code"]]);

        var (row, tab) = (page.Find("[role=\"tablist\"]"), page.Find("[aria-selected=\"true\"]"));

        Assert.True(row.HasClass("h-8.5"));
        Assert.True(tab.HasClass("px-3"));
        Assert.False(tab.HasClass("px-4"));
    }

    [Fact]
    public void An_icon_leads_the_label_and_a_trailing_one_follows_it()
    {
        var row = Ui.Tabs[Ui.Tab.Icon(Ui.IconName.User).IconTrailing(Ui.IconName.ChevronDown)["Profile"]];

        var html = Page.Render(row).Html;

        var (first, label, last) = (html.IndexOf("<svg", StringComparison.Ordinal), html.IndexOf("Profile", StringComparison.Ordinal), html.LastIndexOf("<svg", StringComparison.Ordinal));
        Assert.True(first >= 0 && first < label && label < last);
    }

    [Theory]
    [InlineData(Ui.TabsVariant.Default, "0 0 24 24")]
    [InlineData(Ui.TabsVariant.Segmented, "0 0 20 20")]
    public void An_icon_is_the_outline_in_a_row_and_the_mini_in_a_segment_at_20px(Ui.TabsVariant variant, string box)
    {
        var row = Ui.Tabs.Variant(variant)[Ui.Tab.Icon(Ui.IconName.ListBullet)["List"]];

        var icon = Page.Render(row).Find("[data-ui-icon]");

        Assert.Equal(box, icon.Attribute("viewBox"));
        Assert.True(icon.HasClass("size-5"));
    }

    [Fact]
    public void A_scrollable_row_scrolls_inside_its_own_box_and_leaves_the_tablist_whole()
    {
        var page = Page.Render(Three(Ui.Tabs.Scrollable()));

        var area = page.Find("[role=\"tablist\"]").Parent!.Parent!;

        Assert.True(area.HasClass("overflow-auto"));
        Assert.False(area.HasClass("ui-tabs-fade"));
        Assert.Equal(3, page.FindAll("[role=\"tablist\"] > [role=\"tab\"]").Count);
    }

    [Fact]
    public void A_scrollable_row_fades_its_edge_and_hides_its_scrollbar_only_when_asked()
    {
        var page = Page.Render(Three(Ui.Tabs.Scrollable().ScrollableFade().ScrollableScrollbar(Ui.TabsScrollbar.Hide)));

        var area = page.Find("[role=\"tablist\"]").Parent!.Parent!;

        Assert.True(area.HasClass("ui-tabs-fade"));
        Assert.True(area.HasClass("[scrollbar-width:none]"));
    }

    [Fact]
    public void A_tab_outside_a_row_is_selected_only_when_it_says_so()
    {
        // Two of them, because a tab with no row to ask must not be answered by the one before it.
        var page = Page.Render(Div[Ui.Tab.Selected()["One"], Ui.Tab.Selected()["Two"], Ui.Tab["Three"]]);

        var selected = page.FindAll("[role=\"tab\"]").Select(tab => tab.Attribute("aria-selected"));

        Assert.Equal(["true", "true", "false"], selected);
    }

    [Fact]
    public void Nothing_daisyUI_draws_is_left_in_the_markup()
    {
        var html = Page.Render(Three(Ui.Tabs.Segmented)).Html;

        var classes = Page.Render(Three(Ui.Tabs)).Html + html;

        Assert.DoesNotContain("tab-active", classes, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs-box", classes, StringComparison.Ordinal);
        Assert.DoesNotContain("base-", classes, StringComparison.Ordinal);
    }
}
