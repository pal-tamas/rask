namespace Rask.Ui.Tests.Components;

/// <summary>
///     The rotating word list.
/// </summary>
/// <remarks>
///     <para>
///         The rotation is CSS, so every word is in the markup at all times — the animation only
///         decides which one is on screen. That is the whole contract worth asserting here, and it is
///         also an accessibility claim: a reader who never sees the animation, because they use
///         reduced motion or a screen reader, still gets the complete list.
///     </para>
///     <para>
///         This came DOWN from the browser suite. It was the one UiKit end-to-end test of forty-two
///         whose assertions a rendered string could make — its own comment said the words are in the
///         DOM "whatever the browser is doing with them" — so it was paying a published bundle and a
///         Chromium page to read four words out of some markup. The other forty-one stayed: they
///         assert CSS visibility, layout geometry, real input and focus, none of which survive the
///         trip to a string.
///     </para>
/// </remarks>
public partial class UiTextRotateTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Every_word_is_in_the_markup_not_just_the_visible_one()
    {
        var html = UiTextRotate.Words(["fast", "typed", "small", "whole"]).ToHtml();

        Assert.Contains("fast", html, StringComparison.Ordinal);
        Assert.Contains("typed", html, StringComparison.Ordinal);
        Assert.Contains("small", html, StringComparison.Ordinal);
        Assert.Contains("whole", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_words_keep_their_declared_order()
    {
        // Order is the reading order for anyone who sees the list rather than the animation, and the
        // animation's own sequence besides. Asserted by position so a re-render that reshuffles the
        // list is a failure rather than a still-green "all four are present".
        var html = UiTextRotate.Words(["fast", "typed", "small", "whole"]).ToHtml();

        var fast = html.IndexOf("fast", StringComparison.Ordinal);
        var typed = html.IndexOf("typed", StringComparison.Ordinal);
        var small = html.IndexOf("small", StringComparison.Ordinal);
        var whole = html.IndexOf("whole", StringComparison.Ordinal);

        Assert.True(fast < typed && typed < small && small < whole,
            $"the words are out of order in the markup: {html}");
    }

    [Fact]
    public void The_inner_wrapper_survives_because_daisyUI_counts_its_children()
    {
        // Flattening the inner <span> away leaves the words unanimated: daisyUI's selector is `> *`,
        // and it counts THAT element's children to choose the animation. A refactor that "simplifies"
        // one span out of the tree is the failure this pins.
        var html = UiTextRotate.Words(["one", "two"]).ToHtml();

        Assert.Contains("text-rotate", html, StringComparison.Ordinal);
        Assert.Contains("<span><span", html.Replace(" ", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void A_caller_supplied_class_joins_the_kit_class_rather_than_replacing_it()
    {
        var html = UiTextRotate.Words(["one"]).Class("text-4xl").ToHtml();

        Assert.Contains("text-rotate", html, StringComparison.Ordinal);
        Assert.Contains("text-4xl", html, StringComparison.Ordinal);
    }
}
