namespace Rask.Ui.Tests.Components;

/// <summary>
///     The Layout category.
/// </summary>
public partial class UiLayoutTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_drawer_still_keeps_its_state_in_a_checkbox()
    {
        // Not an implementation detail the kit could swap out: daisyUI's rules are written against
        // `.drawer-toggle:checked` and `.drawer-open > .drawer-toggle`, so removing the input removes
        // the component.
        var html = Drawer(open: null);

        Assert.Contains("drawer-toggle", html);
        Assert.Contains("<input", html);
    }

    [Fact]
    public void C_sharp_can_set_the_drawer_open()
    {
        Assert.Contains("checked", Drawer(open: true));
        Assert.DoesNotContain("checked", Drawer(open: false));
    }

    [Fact]
    public void The_overlay_is_a_label_so_a_click_outside_closes_it_with_no_script()
    {
        // The one dismissal a dropdown cannot offer without script.
        var html = Drawer(open: null);

        Assert.Contains("drawer-overlay", html);
        Assert.Contains("aria-label=\"Close navigation\"", html);
    }

    [Fact]
    public void The_page_and_the_panel_are_daisyUIs_two_halves()
    {
        var html = Drawer(open: null);

        Assert.Contains("drawer-content", html);
        Assert.Contains("drawer-side", html);
    }

    [Fact]
    public void A_divider_can_carry_words_or_not()
    {
        Assert.Contains("or", UiDivider.Text("or").ToHtml());
        Assert.Contains("divider", UiDivider.ToHtml());
    }

    [Fact]
    public void An_indicator_puts_its_badge_over_its_child()
    {
        var html = UiIndicator.Badge(UiBadge.Label("9"))[UiButton.Label("Inbox")].ToHtml();

        Assert.Contains("indicator", html);
        Assert.Contains("Inbox", html);
        Assert.Contains("9", html);
    }

    [Fact]
    public void A_join_groups_its_children_into_one_control() =>
        Assert.Contains("join", UiJoin[UiButton.Label("1"), UiButton.Label("2")].ToHtml());

    [Fact]
    public void A_vertical_join_says_so() =>
        Assert.Contains("join-vertical", UiJoin.Vertical(true)[UiButton.Label("1")].ToHtml());

    [Fact]
    public void An_avatar_keeps_its_alt_text()
    {
        // A decorative avatar would take an empty alt; this one takes a required one, because an avatar
        // in a list of people is the only thing saying which person the row is about.
        Assert.Contains("alt=\"Ada\"", UiAvatar.Src("/me.png").Alt("Ada").ToHtml());
    }

    [Fact]
    public void A_kbd_is_a_kbd_element() =>
        Assert.Contains("<kbd", UiKbd.Text("K").ToHtml());

    [Fact]
    public void A_hero_and_a_stack_carry_their_base_classes()
    {
        Assert.Contains("hero", UiHero[Span["x"]].ToHtml());
        Assert.Contains("stack", UiStack[Span["x"]].ToHtml());
    }

    [Fact]
    public void A_footer_can_run_horizontally() =>
        Assert.Contains("footer-horizontal", UiFooter.Horizontal(true)[Span["x"]].ToHtml());

    [Fact]
    public void The_shell_carries_the_theme_scope_and_follows_the_OS_by_default()
    {
        var html = UiShell[Span["x"]].ToHtml();

        // Without the attribute nothing inside has a colour at all — daisyUI's palette is confined to it
        // so that referencing this package cannot repaint an app that only wanted a button.
        Assert.Contains(UiStylesheet.ThemeScopeAttribute, html, StringComparison.Ordinal);

        // And NO data-theme, which is not the same as an empty one: daisyUI's OS-following rule is
        // `[data-rask-ui]:not([data-theme])`, so an attribute present with an unmatched value would
        // leave the subtree with no palette rather than with the system's.
        Assert.DoesNotContain("data-theme", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shell_can_name_its_theme()
    {
        // The shell IS the element carrying the scope, so this is the only place a surface built on it
        // can say so — writing data-theme on an ancestor loses to the rule above, which matches here.
        Assert.Contains(
            "data-theme=\"dark\"",
            UiShell.Theme(UiThemeName.Dark)[Span["x"]].ToHtml(),
            StringComparison.Ordinal);
    }

    private static string Drawer(bool? open) =>
        UiDrawer
            .Id("nav")
            .Side(UiMenu[UiMenuItem.Text("Home").Href("/")])
            .Open(open)
            .CloseLabel("Close navigation")[Span["page"]]
            .ToHtml();
}
