using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     What a dropdown and each kind of menu row writes:
///     <c>Ui.Dropdown[trigger, Ui.Menu[Ui.MenuItem…]]</c>, Flux UI's <c>flux:dropdown</c> and <c>flux:menu</c>.
/// </summary>
/// <remarks>
///     The markup contract: the roles and ARIA a screen reader hears, the attributes the browser and the runtime
///     act on, and the <c>data-ui-*</c> markers the stylesheet and the parity tool key on. How it LOOKS is held to
///     Flux by <c>scripts/flux/parity.mjs</c> and <c>parity-menu.mjs</c>; what the keys do, by
///     <see cref="UiDropdownInteractionTests" />.
/// </remarks>
public partial class UiDropdownTests : global::Rask.Core.RaskMarkup
{
    private static string Html(global::Rask.Core.Component menu) => Ui.Dropdown[Button["Options"], menu].ToHtml().AsText();

    private static string Row(string html, string words) =>
        Regex.Match(html, "<(button|a)\\b[^>]*>(?:(?!</\\1>).)*?" + Regex.Escape(words) + ".*?</\\1>", RegexOptions.Singleline).Value;

    [Fact]
    public void The_first_child_becomes_the_button_that_opens_the_menu()
    {
        var html = Ui.Dropdown[Button.Class("mine").Aria("label", "More")["Options"], Ui.Menu[Ui.MenuItem["Edit"]]].ToHtml().AsText();

        var trigger = Regex.Match(html, "<button[^>]*>Options").Value;
        var panel = Regex.Match(trigger, "popovertarget=\"([^\"]+)\"").Groups[1].Value;

        Assert.Matches("<div[^>]*data-ui-dropdown=\"\"", html);
        // Flux's own value, whatever the trigger opens.
        Assert.Contains("aria-haspopup=\"true\"", trigger, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", trigger, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"" + panel + "\"", trigger, StringComparison.Ordinal);
        // It is the caller's element: what they put on it is still there.
        Assert.Contains("class=\"mine\"", trigger, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"More\"", trigger, StringComparison.Ordinal);
        Assert.Matches("<div id=\"" + Regex.Escape(panel) + "\"[^>]*popover=\"auto\"", html);
    }

    [Fact]
    public void The_stylesheet_no_longer_carries_the_dropdown_it_replaced()
    {
        var css = UiStylesheet.Css;

        // The bare word in a kit comment would bring daisyUI's class back; the plugin's `exclude` keeps it out.
        // (`.menu .dropdown-content` is daisyUI's menu styling one inside it, and stays with that menu.)
        Assert.DoesNotContain(".dropdown{", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".dropdown-end", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".dropdown-open", css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trigger_with_a_tooltip_still_opens_the_menu_from_its_own_button()
    {
        var html = Ui.Dropdown[Ui.Button.Tooltip("More")["Options"], Ui.Menu[Ui.MenuItem["Edit"]]].ToHtml().AsText();

        // Flux wraps such a button in its tooltip; what the dropdown adds belongs on the button inside it.
        var wrapper = Regex.Match(html, "<div[^>]*data-ui-tooltip[ >=][^>]*>").Value;
        var trigger = Regex.Match(html, "<button.*?>Options").Value;
        var panel = Regex.Match(trigger, "popovertarget=\"([^\"]+)\"").Groups[1].Value;

        Assert.NotEmpty(wrapper);
        Assert.DoesNotContain("popovertarget", wrapper, StringComparison.Ordinal);
        Assert.Contains("aria-haspopup=\"true\"", trigger, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"" + panel + "\"", trigger, StringComparison.Ordinal);
        Assert.Matches("<div id=\"" + Regex.Escape(panel) + "\"[^>]*data-ui-menu=\"\"", html);
    }

    [Fact]
    public void The_menu_is_a_popover_that_takes_focus_as_it_opens()
    {
        var html = Html(Ui.Menu[Ui.MenuItem["Edit"]]);

        var menu = Regex.Match(html, "<div[^>]*data-ui-menu=\"\"[^>]*>").Value;

        Assert.Contains("role=\"menu\"", menu, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"-1\"", menu, StringComparison.Ordinal);
        Assert.Contains("popover=\"auto\"", menu, StringComparison.Ordinal);
        Assert.Contains(" autofocus", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void It_opens_below_its_trigger_from_the_start_edge_five_pixels_off()
    {
        var html = Html(Ui.Menu[Ui.MenuItem["Edit"]]);

        var anchor = Regex.Match(html, "anchor-name:(--uidd-\\d+)").Groups[1].Value;

        Assert.Contains("position-anchor:" + anchor, html, StringComparison.Ordinal);
        Assert.Contains("inset-block-start:calc(anchor(end) + 5px)", html, StringComparison.Ordinal);
        Assert.Contains("inset-inline-start:calc(anchor(start) + 0px)", html, StringComparison.Ordinal);
        Assert.Contains("position-try-fallbacks:flip-block", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.DropdownPosition.Top, Ui.DropdownAlign.End, "inset-block-end:calc(anchor(start) + 2px);inset-inline-end:calc(anchor(end) + -15px)")]
    [InlineData(Ui.DropdownPosition.Bottom, Ui.DropdownAlign.Center, "inset-block-start:calc(anchor(end) + 2px);inset-inline-start:calc(anchor(center) + -15px);translate:-50% 0")]
    [InlineData(Ui.DropdownPosition.Right, Ui.DropdownAlign.Start, "inset-inline-start:calc(anchor(end) + 2px);inset-block-start:calc(anchor(start) + -15px)")]
    [InlineData(Ui.DropdownPosition.Left, Ui.DropdownAlign.Center, "inset-inline-end:calc(anchor(start) + 2px);inset-block-start:calc(anchor(center) + -15px);translate:0 -50%")]
    public void Position_picks_the_side_and_Align_the_edge_with_the_gap_and_offset_measured_from_the_trigger(
        Ui.DropdownPosition position, Ui.DropdownAlign align, string expected)
    {
        var dropdown = Ui.Dropdown.Position(position).Align(align).Gap(2).Offset(-15)[Button["Options"], Ui.Menu[Ui.MenuItem["Edit"]]];

        var html = dropdown.ToHtml().AsText();

        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_is_a_menuitem_button_with_its_words_and_nothing_else()
    {
        var html = Html(Ui.Menu[Ui.MenuItem["Edit"]]);

        var row = Row(html, "Edit");

        Assert.StartsWith("<button", row, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", row, StringComparison.Ordinal);
        Assert.Contains("role=\"menuitem\"", row, StringComparison.Ordinal);
        Assert.Contains("data-ui-menu-item=\"\"", row, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"-1\"", row, StringComparison.Ordinal);
        // The room an icon would take, which the stylesheet shows only when a sibling row has one.
        Assert.Contains("data-indent=\"\"", row, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", row, StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_comes_before_the_words_and_a_shortcut_suffix_and_trailing_icon_after_them()
    {
        var html = Html(Ui.Menu[
            Ui.MenuItem.Icon(Ui.IconName.PencilSquare).Suffix("3").Kbd("⌘S").IconTrailing(Ui.IconName.ArrowUpRight)["Save"]
        ]);

        var row = Row(html, "Save");
        var words = row.IndexOf("Save", StringComparison.Ordinal);

        Assert.Contains("data-ui-menu-item-has-icon=\"\"", row, StringComparison.Ordinal);
        Assert.DoesNotContain("data-indent", row, StringComparison.Ordinal);
        Assert.Matches("<svg[^>]*data-ui-menu-item-icon", row);
        Assert.InRange(row.IndexOf("<svg", StringComparison.Ordinal), 0, words);
        Assert.InRange(row.IndexOf(">3<", StringComparison.Ordinal), words, row.Length);
        Assert.InRange(row.IndexOf(">⌘S<", StringComparison.Ordinal), words, row.Length);
        Assert.InRange(row.LastIndexOf("<svg", StringComparison.Ordinal), row.IndexOf(">⌘S<", StringComparison.Ordinal), row.Length);
        // Flux draws a menu's icons from the 20px set.
        Assert.Contains("viewBox=\"0 0 20 20\"", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Danger_is_red_under_the_pointer_and_under_the_cursor()
    {
        var html = Html(Ui.Menu[Ui.MenuItem.Danger["Delete"], Ui.MenuItem["Edit"]]);

        var danger = Row(html, "Delete");

        Assert.Contains("hover:text-red-600", danger, StringComparison.Ordinal);
        Assert.Contains("data-active:text-red-600", danger, StringComparison.Ordinal);
        Assert.DoesNotContain("text-red-600", Row(html, "Edit"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_item_is_a_disabled_button_and_runs_nothing()
    {
        var html = Html(Ui.Menu[Ui.MenuItem.Disabled(true).OnClick(() => { }).Href("/away")["Archive"]]);

        var row = Row(html, "Archive");

        Assert.StartsWith("<button", row, StringComparison.Ordinal);
        Assert.Contains(" disabled", row, StringComparison.Ordinal);
        Assert.DoesNotContain("href", row, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on", row, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_with_an_Href_is_a_link_that_is_still_a_menuitem()
    {
        var html = Html(Ui.Menu[Ui.MenuItem.Href("/settings")["Settings"]]);

        var row = Row(html, "Settings");

        Assert.StartsWith("<a", row, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings\"", row, StringComparison.Ordinal);
        Assert.Contains("role=\"menuitem\"", row, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepOpen_is_said_where_it_was_asked_for()
    {
        var onTheMenu = Html(Ui.Menu.KeepOpen(true)[Ui.MenuItem["Edit"]]);
        var onTheRows = Html(Ui.Menu[
            Ui.MenuItem.KeepOpen(true)["Edit"],
            Ui.MenuCheckbox.Value(true).KeepOpen(true)["Draft"],
            Ui.MenuRadioGroup.Value("a").KeepOpen(true)[Ui.MenuRadio.Value("a").KeepOpen(true)["Name"]],
            Ui.MenuSubmenu.Heading("More").KeepOpen(true)[Ui.MenuItem["Deep"]],
            Ui.MenuItem["Close"]
        ]);

        // The runtime closes the popover on a pick unless the row, or something around it, carries this.
        Assert.Matches("<div[^>]*data-ui-menu=\"\"[^>]*data-rask-keep-open=\"\"[^>]*role=\"menu\"", onTheMenu);
        Assert.Contains("data-rask-keep-open", Row(onTheRows, "Edit"), StringComparison.Ordinal);
        Assert.Contains("data-rask-keep-open", Row(onTheRows, "Draft"), StringComparison.Ordinal);
        Assert.Contains("data-rask-keep-open", Row(onTheRows, "Name"), StringComparison.Ordinal);
        Assert.Matches("<div[^>]*data-ui-menu-radio-group=\"\"[^>]*data-rask-keep-open=\"\"", onTheRows);
        Assert.Matches("<div[^>]*data-ui-menu=\"\"[^>]*data-rask-keep-open=\"\"[^>]*role=\"menu\"", onTheRows);
        Assert.DoesNotContain("data-rask-keep-open", Row(onTheRows, "Close"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_checkbox_row_says_whether_it_is_checked_and_draws_its_mark_only_then()
    {
        var html = Html(Ui.Menu[Ui.MenuCheckbox.Value(true)["Draft"], Ui.MenuCheckbox.Value(false)["Archived"]]);

        var on = Row(html, "Draft");
        var off = Row(html, "Archived");

        Assert.Contains("role=\"menuitemcheckbox\"", on, StringComparison.Ordinal);
        Assert.Contains("aria-checked=\"true\"", on, StringComparison.Ordinal);
        Assert.Contains("data-checked=\"\"", on, StringComparison.Ordinal);
        Assert.Contains("data-ui-menu-checkbox=\"\"", on, StringComparison.Ordinal);
        Assert.Contains("aria-checked=\"false\"", off, StringComparison.Ordinal);
        Assert.DoesNotContain("data-checked", off, StringComparison.Ordinal);
        // The mark keeps its room either way, so the words line up; unchecked, it is not displayed.
        Assert.Contains("<div class=\"w-7\"><div><svg", on, StringComparison.Ordinal);
        Assert.Contains("<div class=\"w-7\"><div class=\"hidden\"><svg", off, StringComparison.Ordinal);
    }

    [Fact]
    public void A_radio_group_checks_the_radio_whose_value_is_its_own()
    {
        var html = Html(Ui.Menu[
            Ui.MenuRadioGroup.Value("date")[
                Ui.MenuRadio.Value("name")["Name"],
                Ui.MenuRadio.Value("date")["Date"]
            ]
        ]);

        var chosen = Row(html, "Date");

        Assert.Matches("<div[^>]*data-ui-menu-radio-group=\"\"[^>]*role=\"group\"", html);
        Assert.Contains("role=\"menuitemradio\"", chosen, StringComparison.Ordinal);
        Assert.Contains("aria-checked=\"true\"", chosen, StringComparison.Ordinal);
        Assert.Contains("data-checked=\"\"", chosen, StringComparison.Ordinal);
        Assert.Contains("aria-checked=\"false\"", Row(html, "Name"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_radio_outside_a_group_is_checked_when_it_says_so()
    {
        var html = Html(Ui.Menu[Ui.MenuRadio.Checked(true)["Name"], Ui.MenuRadio["Date"]]);

        var name = Row(html, "Name");

        Assert.Contains("aria-checked=\"true\"", name, StringComparison.Ordinal);
        Assert.Contains("aria-checked=\"false\"", Row(html, "Date"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_checkbox_group_is_a_group_to_a_screen_reader()
    {
        var html = Html(Ui.Menu[Ui.MenuCheckboxGroup[Ui.MenuCheckbox.Value(true)["Draft"]]]);

        var group = Regex.Match(html, "<div[^>]*data-ui-menu-checkbox-group=\"\"[^>]*>").Value;

        Assert.Contains("role=\"group\"", group, StringComparison.Ordinal);
    }

    [Fact]
    public void A_separator_is_a_line_and_not_a_row()
    {
        var html = Html(Ui.Menu[Ui.MenuItem["Edit"], Ui.MenuSeparator, Ui.MenuItem["Delete"]]);

        var line = Regex.Match(html, "<div[^>]*data-ui-menu-separator=\"\"[^>]*>.*?</div></div>", RegexOptions.Singleline).Value;

        Assert.Contains("role=\"none\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("menuitem", line, StringComparison.Ordinal);
        Assert.DoesNotContain("tabindex", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_names_its_rows_and_carries_the_two_lines_the_stylesheet_chooses_between()
    {
        var html = Html(Ui.Menu[Ui.MenuGroup.Heading("Account")[Ui.MenuItem["Profile"]], Ui.MenuGroup[Ui.MenuItem["Logout"]]]);

        var groups = Regex.Matches(html, "<div[^>]*data-ui-menu-group=\"\"[^>]*role=\"group\"");

        Assert.Equal(2, groups.Count);
        Assert.Matches("<div[^>]*data-ui-menu-heading=\"\"[^>]*>.*?Account", html);
        Assert.Single(Regex.Matches(html, "data-ui-menu-heading"));
        Assert.Equal(2, Regex.Matches(html, "data-ui-menu-separator-top").Count);
        Assert.Equal(2, Regex.Matches(html, "data-ui-menu-separator-bottom").Count);
    }

    [Fact]
    public void A_submenu_row_carries_no_aria_of_its_own_and_its_flyout_is_the_menu_right_after_it()
    {
        var html = Html(Ui.Menu[Ui.MenuSubmenu.Heading("Sort by").Icon(Ui.IconName.ArrowsUpDown)[Ui.MenuItem["Name"]]]);

        var row = Row(html, "Sort by");

        Assert.Matches("<div[^>]*data-ui-menu-submenu=\"\"", html);
        Assert.Contains("role=\"menuitem\"", row, StringComparison.Ordinal);
        // As Flux writes it: the row says nothing about the flyout, and the flyout has no name and no id.
        Assert.DoesNotMatch(" aria-[a-z]+=", Regex.Match(row, "<button[^>]*>").Value);
        Assert.Contains("data-ui-menu-item-has-icon", row, StringComparison.Ordinal);
        Assert.Matches("</button><div class=\"[^\"]*\" data-ui-menu=\"\" role=\"menu\" tabindex=\"-1\">", html);
        // The flyout is shown by the stylesheet, not by the top layer: only the menu itself is a popover.
        Assert.Single(Regex.Matches(html, "popover=\"auto\""));
    }

    [Fact]
    public void A_navmenu_is_a_nav_of_plain_links_with_no_menu_roles_and_no_cursor()
    {
        var html = Ui.Dropdown.End[
            Button["Olivia Martin"],
            Ui.Navmenu[
                Ui.NavmenuItem.Href("/account").Icon(Ui.IconName.User)["Account"],
                Ui.NavmenuItem.Href("/delete").Danger["Delete"]
            ]
        ].ToHtml().AsText();

        var link = Row(html, "Account");

        Assert.Matches("<nav[^>]*popover=\"auto\"[^>]*data-ui-navmenu=\"\"", html);
        Assert.StartsWith("<a", link, StringComparison.Ordinal);
        Assert.Contains("href=\"/account\"", link, StringComparison.Ordinal);
        Assert.Contains("data-ui-navmenu-item=\"\"", link, StringComparison.Ordinal);
        Assert.DoesNotContain("role=", html.Split("<nav", 2)[1], StringComparison.Ordinal);
        Assert.DoesNotContain("autofocus", html, StringComparison.Ordinal);
        // Flux's own bare "true": what opens is a list of links, not a `menu`.
        Assert.Contains("aria-haspopup=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("inset-inline-end:calc(anchor(end) + 0px)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_dropdowns_on_a_page_do_not_share_an_id()
    {
        var html = Div[
            Ui.Dropdown[Button["One"], Ui.Menu[Ui.MenuItem["A"]]],
            Ui.Dropdown[Button["Two"], Ui.Menu[Ui.MenuItem["B"]]]
        ].ToHtml().AsText();

        var ids = Regex.Matches(html, " id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, Regex.Matches(html, "popovertarget=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_page_that_owns_Open_has_the_runtime_show_or_hide_the_menu_to_match()
    {
        var uncontrolled = Html(Ui.Menu[Ui.MenuItem["Edit"]]);

        var open = Ui.Dropdown.Open(true)[Button["Options"], Ui.Menu[Ui.MenuItem["Edit"]]].ToHtml().AsText();

        Assert.DoesNotContain("data-rask-popover-open", uncontrolled, StringComparison.Ordinal);
        Assert.Contains("data-rask-popover-open=\"true\"", open, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"true\"", open, StringComparison.Ordinal);
        Assert.Matches("<div[^>]*data-ui-dropdown=\"\"[^>]*data-open=\"\"", open);
    }

    [Fact]
    public void A_hover_dropdown_asks_the_runtime_to_open_its_menu_under_the_pointer()
    {
        var hover = Ui.Dropdown.Hover()[Button["Options"], Ui.Menu[Ui.MenuItem["Edit"]]].ToHtml().AsText();
        var plain = Html(Ui.Menu[Ui.MenuItem["Edit"]]);

        Assert.Matches("<div[^>]* data-ui-dropdown[^>]* data-rask-hover=\"(uidd-\\d+-panel)\"", hover);
        Assert.Matches("<button[^>]* popovertarget=\"uidd-\\d+-panel\"", hover);
        Assert.DoesNotContain("data-rask-hover", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void A_navmenu_item_with_nowhere_to_go_is_a_button()
    {
        var html = Ui.Navmenu[Ui.NavmenuItem.Icon(Ui.IconName.ArrowTurnDownRight)["Team"]].ToHtml().AsText();

        Assert.Matches("<button[^>]* data-ui-navmenu-item[^>]* type=\"button\"", html);
        Assert.Matches("<svg[^>]* data-navmenu-icon", html);
        Assert.DoesNotContain("<a", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_navmenu_does_not_take_the_ink_of_what_it_hangs_from()
    {
        var html = Ui.Navmenu[Ui.NavmenuItem["Team"]].ToHtml().AsText();

        Assert.Contains("text-black", html, StringComparison.Ordinal);
        Assert.Contains("dark:text-white", html, StringComparison.Ordinal);
        Assert.DoesNotContain("text-inherit", html, StringComparison.Ordinal);
    }
}
