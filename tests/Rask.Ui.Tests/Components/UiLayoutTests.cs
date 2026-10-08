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
}
