namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiList" /> IS its element.
/// </summary>
/// <remarks>
///     It derives from <c>Element</c>, so every step an element takes works on it with nothing redeclared. The
///     class assertions check COMPOSITION rather than a literal: the kit's classes are the kit's business,
///     and what must not change is that a call site's <c>.Class(…)</c> is added to them.
/// </remarks>
public partial class UiListTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_list_is_a_ul_by_default()
    {
        var html = Ui.List[Li["one"]].ToHtml();

        Assert.StartsWith("<ul ", html, StringComparison.Ordinal);
        Assert.Contains("<li>one</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("list-decimal", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordered_list_is_an_ol_and_shows_its_numbers()
    {
        // The difference is meaning, not styling — and the reset every Tailwind app ships strips the
        // markers, so an ordered list that did not turn them back on would draw no ordinals at all.
        var html = Ui.List.Ordered(true)[Li["first"], Li["second"]].ToHtml();

        Assert.StartsWith("<ol ", html, StringComparison.Ordinal);
        Assert.EndsWith("</ol>", html, StringComparison.Ordinal);
        Assert.Contains("list-decimal", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Element_steps_reach_the_list_too()
    {
        var html = Ui.List.Id("log").Class("text-sm").Data("testid", "log").ToHtml();

        Assert.Contains("id=\"log\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"log\"", html, StringComparison.Ordinal);
        Assert.Matches("class=\"ui-list [^\"]* text-sm\"", html);
    }
}
