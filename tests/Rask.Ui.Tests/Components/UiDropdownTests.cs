namespace Rask.UiTests.Components;

/// <summary>
///     The dropdown's markup: a menu button over a popover menu, placed by anchor positioning.
/// </summary>
/// <remarks>
///     What only exists after an event — the cursor, submenus, type-ahead, the open state C# hears — is
///     <c>UiDropdownInteractionTests</c>'.
/// </remarks>
public partial class UiDropdownTests : global::Rask.Core.RaskMarkup
{
    private static string Menu() =>
        Ui.Dropdown.Trigger("Actions")[
            Ui.MenuItem.Key("edit").Text("Edit").Kbd("⌘E"),
            Ui.MenuItem.Key("delete").Text("Delete").Tone(Ui.Tone.Error)
        ].ToHtml();

    [Fact]
    public void The_trigger_is_a_menu_button_that_names_its_popover()
    {
        var html = Menu();
        var trigger = Tag(html, "<button id=\"uidd-");

        Assert.Contains("aria-haspopup=\"menu\"", trigger);
        Assert.Contains("aria-expanded=\"false\"", trigger);
        Assert.Contains("popovertarget=\"uidd-", trigger);
        Assert.Contains("aria-controls=\"uidd-", trigger);
    }

    [Fact]
    public void The_panel_is_a_popover_and_the_menu_inside_it_takes_focus_when_it_opens()
    {
        // Popover focusing steps move focus to the autofocus descendant, so the arrow keys reach the menu straight
        // from the click that opened it. `menu` on the list, never on the popover: a display class on a popover
        // keeps it on screen while closed.
        var html = Menu();

        Assert.Contains("popover=\"auto\"", Tag(html, "<div id=\"uidd-"));
        Assert.DoesNotContain("menu", Tag(html, "<div id=\"uidd-").Replace("uidd-", "", StringComparison.Ordinal));
        var menu = Tag(html, "<ul id=\"uidd-");
        Assert.Contains("role=\"menu\"", menu);
        Assert.Contains("tabindex=\"-1\"", menu);
        Assert.Contains("autofocus", menu);
        Assert.Contains("aria-labelledby=\"uidd-", menu);
    }

    [Fact]
    public void Items_inside_a_dropdown_are_menu_items_with_ids_and_no_tab_stops()
    {
        var html = Menu();

        Assert.Contains("role=\"none\"", html);
        Assert.Equal(2, CountOf(html, "role=\"menuitem\""));
        Assert.Contains("-mi-0\"", html);
        Assert.Contains("-mi-1\"", html);
        // The menu holds focus and moves a cursor; the rows are not Tab stops of their own.
        Assert.Contains("tabindex=\"-1\"", Tag(html, "<button id=\"uidd-" + FirstIdContaining(html, "-mi-0")));
        Assert.Contains("tabindex=\"-1\"", Tag(html, "<button id=\"uidd-" + FirstIdContaining(html, "-mi-1")));
    }

    [Fact]
    public void An_item_outside_a_dropdown_is_an_ordinary_link_or_button()
    {
        // A navigation list: no menu roles, no ids, no cursor.
        var html = Ui.Menu[Ui.MenuItem.Key("home").Text("Home").Href("/").Active(true)].ToHtml();

        Assert.DoesNotContain("role=", html);
        Assert.Contains("aria-current=\"page\"", html);
        Assert.Contains("menu-active", html);
    }

    [Fact]
    public void A_shortcut_a_danger_tone_and_a_trailing_icon_render_on_the_row()
    {
        var html = Ui.Dropdown.Trigger("Actions")[
            Ui.MenuItem.Key("edit").Text("Edit").Kbd("⌘E").IconTrailing(Ui.IconName.ChevronRight),
            Ui.MenuItem.Key("delete").Text("Delete").Tone(Ui.Tone.Error)
        ].ToHtml();

        Assert.Contains("<kbd class=\"kbd kbd-xs ui-menu-kbd\">", html);
        Assert.Contains("text-error", html);
        Assert.True(
            html.IndexOf("Edit", StringComparison.Ordinal) < html.IndexOf("<kbd", StringComparison.Ordinal),
            "the shortcut comes after the words.");
    }

    [Theory]
    [InlineData(null, null, "position-area:block-end span-inline-end")]
    [InlineData(Ui.Position.Top, Ui.Align.End, "position-area:block-start span-inline-start")]
    [InlineData(Ui.Position.Right, Ui.Align.Center, "position-area:inline-end center")]
    [InlineData(Ui.Position.Left, Ui.Align.Start, "position-area:inline-start span-block-end")]
    public void Position_and_align_become_one_anchor_position(Ui.Position? position, Ui.Align? align, string expected) =>
        Assert.Contains(expected, Ui.Dropdown.Trigger("Actions").Position(position).Align(align).ToHtml());

    [Fact]
    public void Gap_and_offset_move_the_panel()
    {
        var html = Ui.Dropdown.Trigger("Actions").Gap(8).Offset(12).ToHtml();

        Assert.Contains("margin:8px", html);
        Assert.Contains("translate:12px 0", html);
        // Flux's default gap when unset.
        Assert.Contains("margin:4px", Ui.Dropdown.Trigger("Actions").ToHtml());
    }

    [Fact]
    public void A_controlled_dropdown_tells_the_runtime_which_state_to_show()
    {
        Assert.Contains("data-rask-popover-open=\"true\"", Ui.Dropdown.Trigger("Actions").Open(true).ToHtml());
        Assert.Contains("data-rask-popover-open=\"false\"", Ui.Dropdown.Trigger("Actions").Open(false).ToHtml());
        Assert.DoesNotContain("data-rask-popover-open", Menu());
        Assert.Contains("aria-expanded=\"true\"", Ui.Dropdown.Trigger("Actions").Open(true).ToHtml());
    }

    [Fact]
    public void Keep_open_is_marked_where_the_runtime_looks_for_it()
    {
        Assert.Contains("data-rask-keep-open", Ui.Dropdown.Trigger("Filters").KeepOpen(true).ToHtml());
        Assert.Contains(
            "data-rask-keep-open",
            Ui.Dropdown.Trigger("Actions")[Ui.MenuItem.Key("pin").Text("Pin").KeepOpen(true)].ToHtml());
        Assert.DoesNotContain("data-rask-keep-open", Menu());
    }

    [Fact]
    public void The_trigger_icon_after_the_label_can_be_replaced()
    {
        // Instance ids differ per render, so they are blanked first — otherwise these would differ for that alone.
        static string Trigger(string html) =>
            System.Text.RegularExpressions.Regex.Replace(html.Split("<div id=\"uidd-")[0], @"uidd-\d+", "uidd");

        Assert.Equal(Trigger(Ui.Dropdown.Trigger("Actions").ToHtml()), Trigger(Ui.Dropdown.Trigger("Actions").ToHtml()));
        Assert.NotEqual(
            Trigger(Ui.Dropdown.Trigger("Actions").ToHtml()),
            Trigger(Ui.Dropdown.Trigger("Actions").IconTrailing(Ui.IconName.ChevronUpDown).ToHtml()));
    }

    [Fact]
    public void Opening_on_hover_keeps_the_daisyUI_css_dropdown()
    {
        // CSS cannot open a popover, so hovering to open is the one shape that stays daisyUI's.
        var html = Ui.Dropdown.Trigger("Actions").OpenOn(Ui.OpenOn.Hover).Align(Ui.Align.End).ToHtml();

        Assert.Contains("dropdown-hover", html);
        Assert.Contains("dropdown-end", html);
        Assert.DoesNotContain("popover=", html);
    }

    [Fact]
    public void A_submenu_is_a_menu_item_that_owns_a_nested_menu()
    {
        var html = Ui.Dropdown.Trigger("Actions")[
            Ui.MenuSub.Key("sort").Heading("Sort by")[
                Ui.MenuItem.Key("name").Text("Name")
            ]
        ].ToHtml();

        var trigger = Tag(html, "<button id=\"uidd-" + FirstIdContaining(html, "-mi-0"));
        Assert.Contains("aria-haspopup=\"menu\"", trigger);
        Assert.Contains("aria-expanded=\"false\"", trigger);
        Assert.Contains("ui-menu-flyout", html);
        Assert.Equal(2, CountOf(html, "role=\"menu\""));
    }

    [Fact]
    public void A_submenu_in_a_plain_menu_is_a_disclosure()
    {
        var html = Ui.Menu[Ui.MenuSub.Key("more").Heading("More")[Ui.MenuItem.Key("a").Text("A").Href("/a")]].ToHtml();

        Assert.Contains("<details>", html);
        Assert.Contains("<summary>", html);
        Assert.DoesNotContain("role=", html);
    }

    [Fact]
    public void Checkbox_and_radio_items_say_whether_they_are_checked()
    {
        var html = Ui.Dropdown.Trigger("View")[
            Ui.MenuCheckbox.Value(true).Key("archived").Text("Show archived"),
            Ui.MenuSeparator.Key("sep"),
            Ui.MenuRadioGroup.Value("date").Key("sort").Options([("name", "Name"), ("date", "Date")]).Heading("Sort")
        ].ToHtml();

        Assert.Contains("role=\"menuitemcheckbox\"", html);
        Assert.Equal(2, CountOf(html, "role=\"menuitemradio\""));
        Assert.Equal(2, CountOf(html, "aria-checked=\"true\""));
        Assert.Equal(1, CountOf(html, "aria-checked=\"false\""));
        Assert.Equal(2, CountOf(html, "data-checked"));
        Assert.Contains("role=\"separator\"", html);
        Assert.Contains("menu-title", html);
        // A checkbox menu keeps the dropdown open by default: flipping three switches should not mean opening it thrice.
        Assert.Contains("data-rask-keep-open", Tag(html, "<button id=\"uidd-" + FirstIdContaining(html, "-mi-0")));
    }

    [Fact]
    public void Attribute_order_on_a_menu_item_holds()
    {
        // id, class, data-*, role, tabindex, aria-* — the order every element renders in.
        var html = Menu();
        var row = Tag(html, "<button id=\"uidd-" + FirstIdContaining(html, "-mi-1"));

        var id = row.IndexOf(" id=", StringComparison.Ordinal);
        var cls = row.IndexOf(" class=", StringComparison.Ordinal);
        var role = row.IndexOf(" role=", StringComparison.Ordinal);
        var tab = row.IndexOf(" tabindex=", StringComparison.Ordinal);
        Assert.True(id < cls && cls < role && role < tab, row);
    }

    private static string FirstIdContaining(string html, string fragment)
    {
        var at = html.IndexOf(fragment + "\"", StringComparison.Ordinal);
        var start = html.LastIndexOf("uidd-", at, StringComparison.Ordinal) + 5;
        return html[start..at] + fragment;
    }

    private static int CountOf(string html, string needle)
    {
        var count = 0;
        for (var at = html.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = html.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // The opening tag that starts with `prefix`, so an assertion about one element cannot pass on another's.
    private static string Tag(string html, string prefix)
    {
        var start = html.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no tag starting {prefix}");
        return html[start..(html.IndexOf('>', start) + 1)];
    }
}
