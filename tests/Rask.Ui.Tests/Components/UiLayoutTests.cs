namespace Rask.UiTests.Components;

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
        Assert.Contains("or", Ui.Divider.Text("or").ToHtml());
        Assert.Contains("divider", Ui.Divider.ToHtml());
    }

    [Fact]
    public void An_indicator_puts_its_badge_over_its_child()
    {
        var html = Ui.Indicator.Badge(Ui.Badge["9"])[Ui.Button["Inbox"]].ToHtml();

        Assert.Contains("indicator", html);
        Assert.Contains("Inbox", html);
        Assert.Contains("9", html);
    }

    [Fact]
    public void A_join_groups_its_children_into_one_control() =>
        Assert.Contains("join", Ui.Join[Ui.Button["1"], Ui.Button["2"]].ToHtml());

    [Fact]
    public void A_vertical_join_says_so() =>
        Assert.Contains("join-vertical", Ui.Join.Vertical(true)[Ui.Button["1"]].ToHtml());

    [Fact]
    public void An_avatar_keeps_its_alt_text()
    {
        // A decorative avatar would take an empty alt; this one takes a required one, because an avatar
        // in a list of people is the only thing saying which person the row is about.
        Assert.Contains("alt=\"Ada\"", Ui.Avatar.Src("/me.png").Alt("Ada").ToHtml());
    }

    [Theory]
    [InlineData(null, "w-10")]
    [InlineData(Ui.Size.Xs, "w-6")]
    [InlineData(Ui.Size.Xl, "w-24")]
    public void An_avatar_is_sized_on_the_kit_axis(Ui.Size? size, string expected)
    {
        // A literal the kit's own sheet is built from, not a class string from the call site that nothing
        // compiled.
        Assert.Contains(expected, Ui.Avatar.Src("/me.png").Alt("Ada").Size(size).ToHtml());
    }

    [Fact]
    public void A_drawer_opens_from_the_right_only_when_asked()
    {
        Assert.DoesNotContain("drawer-end", Drawer(open: null));
        Assert.Contains(
            "drawer-end",
            Ui.Drawer.Id("nav").Panel(Span["menu"]).Position(Ui.Position.Right)[Span["page"]].ToHtml());
    }

    [Fact]
    public void A_kbd_is_a_kbd_element() =>
        Assert.Contains("<kbd", Ui.Kbd.Text("K").ToHtml());

    [Fact]
    public void A_hero_and_a_stack_carry_their_base_classes()
    {
        Assert.Contains("hero", Ui.Hero[Span["x"]].ToHtml());
        Assert.Contains("stack", Ui.Stack[Span["x"]].ToHtml());
    }

    [Fact]
    public void A_footer_can_run_horizontally() =>
        Assert.Contains("footer-horizontal", Ui.Footer.Horizontal(true)[Span["x"]].ToHtml());

    [Fact]
    public void The_shell_carries_the_theme_scope_and_follows_the_OS_by_default()
    {
        var html = Ui.Shell[Span["x"]].ToHtml();

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
            Ui.Shell.Theme(Ui.ThemeName.Dark)[Span["x"]].ToHtml(),
            StringComparison.Ordinal);
    }

    private static string Drawer(bool? open) =>
        Ui.Drawer
            .Id("nav")
            .Panel(Ui.Menu[Ui.MenuItem.Text("Home").Href("/")])
            .Open(open)
            .CloseLabel("Close navigation")[Span["page"]]
            .ToHtml();
}
