#pragma warning disable RASK014 // test-local components have no generated factories

using Rask.Core;

namespace Rask.Testing.Tests;

/// <summary>
///     The structural surface #610 asked for: find an element, say which one, and assert on what it holds
///     — rather than <c>Assert.Contains("&lt;span class=\"pill\"&gt;3&lt;/span&gt;", page.Html)</c>, which is brittle
///     against the very attribute-order invariant this framework's own suite goes to lengths to pin.
/// </summary>
public partial class StructuralQueryTests : global::Rask.Core.RaskMarkup
{
    private sealed class Card : Component
    {
        protected override Component? Render() =>
            Div.Class("panel shadow-sm")[
                H2.Class("title")["Orders"],
                Ul.Id("items")[
                    Li.Class("item")[Span.Class("pill")["3"], " pending"],
                    Li.Class("item selected")[Span.Class("pill")["7"], " shipped"]
                ],
                Button.Class("action").Data(new Dictionary<string, string?> { ["testid"] = "refresh" })["Refresh"]
            ];
    }

    private static Page<Card> Rendered() => Page.Render(new Card());

    [Fact]
    public void Find_hands_back_the_element_not_just_an_attribute()
    {
        var badge = Rendered().Find("#items li.selected .pill");

        Assert.Equal("span", badge.Tag);
        Assert.Equal("7", badge.TextContent);
        Assert.True(badge.HasClass("pill"));
    }

    [Fact]
    public void FindAll_is_in_document_order()
    {
        var badges = Rendered().FindAll(".pill");

        Assert.Equal(["3", "7"], badges.Select(b => b.TextContent));
    }

    // Both halves of Find's contract, and the reason it isn't "first match wins": a test that silently
    // took the first of several keeps passing after somebody adds a second.
    [Fact]
    public void Find_refuses_to_pick_between_several_matches()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Rendered().Find("li"));

        Assert.Contains("2 elements match", error.Message, StringComparison.Ordinal);
        Assert.Contains("FindAll", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_says_which_part_of_the_selector_failed()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Rendered().Find("#items .missing"));

        // The near-miss is the useful half: "#items matches 1, so the rest is what fails" points at the
        // typo, where "no element matches" only says you were wrong somewhere.
        Assert.Contains("#items", error.Message, StringComparison.Ordinal);
        Assert.Contains("matches 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TextOf_joins_the_children_as_a_reader_sees_them()
    {
        // The <li> is a <span> plus a text node; TextOf reads the whole subtree, which is what an
        // assertion about "the row says 7 shipped" actually means.
        Assert.Equal("7 shipped", Rendered().TextOf("#items li.selected"));
    }

    [Fact]
    public void TextOf_collapses_whitespace_to_what_a_reader_sees()
    {
        var page = Page.Render(new Spaced());

        Assert.Equal("Total 42 items", page.TextOf("#t"));
    }

    private sealed class Spaced : Component
    {
        protected override Component? Render() =>
            P.Id("t")["  Total\n\n  ", Span["42"], "   items  "];
    }

    [Fact]
    public void TestId_finds_the_stable_hook()
    {
        Assert.Equal("Refresh", Rendered().TestId("refresh").TextContent);
    }

    [Fact]
    public void Path_names_the_element_so_a_failure_is_findable()
    {
        Assert.Equal("div.panel.shadow-sm > ul#items > li.item.selected", Rendered().Find("li.selected").Path());
    }

    [Theory]
    [InlineData("li:nth-child(2)", "the pseudo-class ':nth-child(2)'")]
    [InlineData("li + li", "the character '+'")]
    [InlineData("[data-testid=refresh]", "an unquoted value")]
    public void An_unsupported_selector_throws_rather_than_quietly_matching_nothing(string selector, string expected)
    {
        // The whole justification for a subset instead of a partial implementation: a selector that
        // silently matched nothing because ':nth-child' was ignored would turn a green test into a lie.
        var error = Assert.Throws<ArgumentException>(() => Rendered().FindAll(selector));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Contains("does not support", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Attributes_are_decoded_not_as_the_serializer_wrote_them()
    {
        var page = Page.Render(new Quoted());

        Assert.Equal("a \"quoted\" & <angled> title", page.Find("#t").Attribute("title"));
        Assert.Equal("3 < 5 & 5 > 3", page.TextOf("#t"));
    }

    private sealed class Quoted : Component
    {
        protected override Component? Render() =>
            Div.Id("t").Title("a \"quoted\" & <angled> title")["3 < 5 & 5 > 3"];
    }
}
