namespace Rask.UiTests.Components;

/// <summary>
///     The Layout category.
/// </summary>
public partial class UiLayoutTests : global::Rask.Core.RaskMarkup
{

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
}
