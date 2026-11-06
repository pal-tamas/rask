using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiForbidden" />: what a page says, inside the app's frame, to a reader whose account may not open it —
///     Flux's danger callout with the catalog's one sentence.
/// </summary>
public partial class UiForbiddenTests : RaskMarkup
{
    [Fact]
    public void It_is_a_danger_callout_that_alerts_with_the_sentence()
    {
        var page = Ui.Forbidden;

        var html = page.ToHtml().AsText();

        Assert.StartsWith("<div class=\"@container", html, StringComparison.Ordinal);
        Assert.Contains("bg-red-50", html, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains(">You do not have permission to view this page.</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hungarian_reader_reads_the_sentence_a_refused_save_says_too()
    {
        var page = () => Ui.Forbidden.ToHtml().AsText();

        var html = KitCulture.In("hu-HU", page);

        Assert.Contains(">Nincs jogosultsága az oldal megtekintéséhez.</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_that_decides_for_itself_draws_it_in_place_of_its_content()
    {
        var allowed = false;

        var html = Div[!allowed ? Ui.Forbidden : P["the records"]].ToHtml().AsText();

        Assert.Contains("data-ui-callout", html, StringComparison.Ordinal);
        Assert.DoesNotContain("the records", html, StringComparison.Ordinal);
    }
}
