using Rask.Data;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     <see cref="UiHighlight" /> turns FullText's match markers into <c>&lt;mark&gt;</c> without ever treating the
///     stored text as markup.
/// </summary>
public partial class UiHighlightTests : global::Rask.Core.RaskMarkup
{
    private const char S = FullText.MatchStart;
    private const char E = FullText.MatchEnd;

    [Fact]
    public void Each_match_is_wrapped_in_mark() =>
        Assert.Equal(
            "<span><mark>SQLite</mark> is <mark>fast</mark>.</span>",
            UiHighlight.Text($"{S}SQLite{E} is {S}fast{E}.").ToHtml());

    [Fact]
    public void Text_without_markers_renders_as_is() =>
        Assert.Equal("<span>plain</span>", UiHighlight.Text("plain").ToHtml());

    [Fact]
    public void Stored_markup_is_encoded_inside_and_outside_a_match()
    {
        var html = UiHighlight.Text($"<script>x</script> {S}<b>hit</b>{E}").ToHtml();

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("<mark>&lt;b&gt;hit&lt;/b&gt;</mark>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unclosed_match_marks_the_rest_and_a_stray_end_is_dropped() =>
        Assert.Equal(
            "<span>a b<mark>c d</mark></span>",
            UiHighlight.Text($"a{E} b{S}c d").ToHtml());

    [Fact]
    public void Class_reaches_the_wrapper() =>
        Assert.Equal("<span class=\"text-sm\">x</span>", UiHighlight.Text("x").Class("text-sm").ToHtml());

    [Fact]
    public void The_markers_are_the_ones_FullText_emits()
    {
        Assert.Equal(FullText.MatchStart, global::Rask.Ui.UiHighlight.MatchStart);
        Assert.Equal(FullText.MatchEnd, global::Rask.Ui.UiHighlight.MatchEnd);
    }
}
