namespace Rask.UiTests.Components;

/// <summary>
///     Flux's sidebar and its parts: the markup contract the layout's CSS and a keyboard depend on.
/// </summary>
/// <remarks>
///     How it LOOKS is held to Flux by <c>scripts/flux/parity.mjs layouts/sidebar</c>. What is pinned here is
///     what no measurement sees: that each state has somewhere to live with no script (a checkbox, and a label
///     for it), that the parts say what they are to a screen reader, and that C# can own either state.
/// </remarks>
public partial class UiSidebarTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_sidebar_that_is_never_put_away_carries_no_state_at_all()
    {
        var html = Ui.Sidebar[Ui.SidebarNav].ToHtml();

        Assert.Contains("data-ui-sidebar", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<input", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-breakpoint", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-sidebar-backdrop", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mobile_sidebar_keeps_its_open_state_in_a_checkbox_and_closes_from_a_backdrop_label()
    {
        var html = Ui.Sidebar.Collapsible(Ui.SidebarCollapsible.Mobile)[Div].ToHtml();

        Assert.Contains("id=\"sidebar-open\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"sidebar-open\"", html, StringComparison.Ordinal);
        Assert.Contains("data-breakpoint=\"lg\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sidebar-rail", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_collapsible_sidebar_has_a_second_checkbox_for_its_rail()
    {
        var html = Ui.Sidebar.Collapsible(Ui.SidebarCollapsible.Always).Breakpoint(Ui.Breakpoint.Md)[Div].ToHtml();

        Assert.Contains("id=\"sidebar-rail\"", html, StringComparison.Ordinal);
        Assert.Contains("data-breakpoint=\"md\"", html, StringComparison.Ordinal);
        Assert.Contains("sidebar-rail:w-14", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_overlay_asks_the_runtime_to_put_it_away_on_navigation()
    {
        var html = Ui.Sidebar.Collapsible(Ui.SidebarCollapsible.Mobile)[Div].ToHtml();

        Assert.Matches("<input[^>]*id=\"sidebar-open\"[^>]*data-rask-uncheck-on-navigate", html);
        Assert.DoesNotContain("data-rask-persist", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rail_is_remembered_under_the_key_Flux_keeps_it_under()
    {
        var html = Ui.Sidebar.Collapsible(Ui.SidebarCollapsible.Always)[Div].ToHtml();

        Assert.Matches("<input[^>]*id=\"sidebar-rail\"[^>]*data-rask-persist=\"flux-sidebar-collapsed-desktop\"", html);
    }

    [Fact]
    public void A_sidebar_told_not_to_persist_does_not_ask_for_it()
    {
        var html = Ui.Sidebar.Collapsible(Ui.SidebarCollapsible.Always).Persist(false)[Div].ToHtml();

        Assert.Contains("id=\"sidebar-rail\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-persist", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_head_script_restores_the_rail_from_the_same_key_the_hook_writes()
    {
        var html = Ui.SidebarScript.ToHtml();

        Assert.StartsWith("<script>", html, StringComparison.Ordinal);
        Assert.Contains("localStorage.getItem('flux-sidebar-collapsed-desktop')", html, StringComparison.Ordinal);
        Assert.Contains("[data-rask-persist=\"flux-sidebar-collapsed-desktop\"]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropdown_makes_the_sidebar_profile_the_button_that_opens_its_menu()
    {
        var html = Ui.Dropdown[Ui.SidebarProfile.Name("Ada"), Ui.Menu[Ui.MenuItem["Sign out"]]].ToHtml();

        Assert.Matches("<button[^>]*popovertarget=\"uidd-\\d+-panel\"[^>]*>", html);
        Assert.Contains("data-ui-avatar", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_toggle_is_a_keyboard_reachable_label_for_the_open_checkbox()
    {
        var html = Ui.SidebarToggle.ToHtml();

        Assert.Contains("<label", html, StringComparison.Ordinal);
        Assert.Contains("for=\"sidebar-open\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"button\"", html, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Toggle sidebar\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inset_pulls_the_toggle_into_the_padding_on_that_side()
    {
        var html = Ui.SidebarToggle.Inset(Ui.Position.Left).ToHtml();

        Assert.Contains("-ms-2.5", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_collapse_control_closes_the_overlay_on_a_phone_and_narrows_the_rail_on_a_desktop()
    {
        var html = Ui.SidebarCollapse.Tooltip("Hide").ToHtml();

        Assert.Contains("for=\"sidebar-open\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"sidebar-rail\"", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("aria-label=\"Hide\"").Length - 1);
    }

    [Fact]
    public void An_item_is_a_link_whose_label_is_its_children()
    {
        var html = Ui.SidebarItem.Href("/inbox").Icon(Ui.IconName.Inbox).Badge("12")["Inbox"].ToHtml();

        Assert.Contains("<a", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/inbox\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-sidebar-item", html, StringComparison.Ordinal);
        Assert.Contains(">Inbox<", html, StringComparison.Ordinal);
        Assert.Contains(">12<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_can_be_stated_either_way()
    {
        var current = Ui.SidebarItem.Href("/help").Current(true)["Help"].ToHtml();
        var other = Ui.SidebarItem.Href("/help").Current(false)["Help"].ToHtml();

        Assert.Contains("aria-current=\"page\"", current, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-current", other, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_with_no_destination_is_a_button()
    {
        var html = Ui.SidebarItem["Sign out"].ToHtml();

        Assert.Contains("<button", html, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rail_hides_an_items_label_from_the_eye_and_keeps_it_for_a_screen_reader()
    {
        var html = Ui.SidebarItem.Href("/inbox").Icon(Ui.IconName.Inbox).Tooltip("Inbox")["Inbox"].ToHtml();

        Assert.Contains("sidebar-rail:sr-only", html, StringComparison.Ordinal);
        Assert.Contains("title=\"Inbox\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_expandable_group_is_a_native_disclosure_open_unless_told()
    {
        var open = Ui.SidebarGroup.Expandable(true).Heading("Favorites")[Ui.SidebarItem.Href("/a")["A"]].ToHtml();
        var closed = Ui.SidebarGroup.Expandable(true).Heading("Favorites").Expanded(false)[Div].ToHtml();

        Assert.Matches("<details[^>]* open", open);
        Assert.Contains("<summary", open, StringComparison.Ordinal);
        Assert.DoesNotMatch("<details[^>]* open", closed);
    }

    [Fact]
    public void A_plain_group_is_a_heading_over_its_items()
    {
        var html = Ui.SidebarGroup.Heading("Settings")[Ui.SidebarItem.Href("/p")["Profile"]].ToHtml();

        Assert.DoesNotContain("<details", html, StringComparison.Ordinal);
        Assert.Contains(">Settings<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_group_with_an_icon_has_anything_to_show_in_the_rail()
    {
        var with = Ui.SidebarGroup.Expandable(true).Heading("Favorites").Icon(Ui.IconName.Star)[Div].ToHtml();
        var without = Ui.SidebarGroup.Expandable(true).Heading("Favorites")[Div].ToHtml();

        Assert.Contains("data-ui-sidebar-group-dropdown", with, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-sidebar-group-dropdown", without, StringComparison.Ordinal);
    }

    [Fact]
    public void The_search_is_a_button_until_it_is_given_something_to_hear()
    {
        var button = Ui.SidebarSearch.Placeholder("Search...").ToHtml();
        var field = Ui.SidebarSearch.Placeholder("Filter").Value("ab").OnInput(_ => { }).ToHtml();

        Assert.Contains("<button", button, StringComparison.Ordinal);
        Assert.Contains("type=\"search\"", field, StringComparison.Ordinal);
        Assert.Contains("value=\"ab\"", field, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Filter\"", field, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nav_is_a_navigation_landmark()
    {
        var html = Ui.SidebarNav[Div].ToHtml();

        Assert.StartsWith("<nav", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Stashable_is_the_old_word_for_a_mobile_sidebar()
    {
        var html = Ui.Sidebar.Stashable(true)[Div].ToHtml();

        Assert.Contains("id=\"sidebar-open\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"sidebar-rail\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_brand_takes_a_second_logo_for_dark_mode()
    {
        var html = Ui.SidebarBrand.Href("#").Logo("/l.png").LogoDark("/d.png").Name("Acme Inc.").ToHtml();

        Assert.Contains("src=\"/l.png\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/d.png\"", html, StringComparison.Ordinal);
        Assert.Contains("dark:hidden", html, StringComparison.Ordinal);
        Assert.Contains(">Acme Inc.<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_is_a_button_with_initials_where_there_is_no_picture()
    {
        var html = Ui.SidebarProfile.Name("Olivia Martin").ToHtml();

        Assert.Contains("<button", html, StringComparison.Ordinal);
        Assert.Contains(">OM<", html, StringComparison.Ordinal);
        Assert.Contains(">Olivia Martin<", html, StringComparison.Ordinal);
    }
}
