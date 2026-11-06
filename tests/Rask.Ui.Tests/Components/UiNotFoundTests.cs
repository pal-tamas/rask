using Rask.Core;
using Rask.Core.Routing;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiNotFound" />: what an address no page answers says inside the app's frame — Flux's warning callout
///     with one sentence and the way home.
/// </summary>
public partial class UiNotFoundTests : RaskMarkup
{
    [Fact]
    public void It_is_a_warning_callout_with_the_sentence_and_a_button_home()
    {
        var page = Ui.NotFound;

        var html = page.ToHtml().AsText();

        Assert.StartsWith("<div class=\"@container", html, StringComparison.Ordinal);
        Assert.Contains("bg-yellow-50", html, StringComparison.Ordinal);
        Assert.Contains(">The page you requested could not be found.</div>", html, StringComparison.Ordinal);
        Assert.Matches("<a [^>]*href=\"/\"[^>]*>.*Back to home.*</a>", html);
    }

    [Fact]
    public void A_hungarian_reader_reads_the_catalogs_sentence_and_the_way_home()
    {
        var page = () => Ui.NotFound.ToHtml().AsText();

        var html = KitCulture.In("hu-HU", page);

        Assert.Contains(">A kért oldal nem található.</div>", html, StringComparison.Ordinal);
        Assert.Contains("Vissza a kezdőlapra", html, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be found", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_leads_the_button_where_the_app_says()
    {
        var page = Ui.NotFound.Home(new RouteUrl("/dashboard"));

        var html = page.ToHtml().AsText();

        Assert.Matches("<a [^>]*href=\"/dashboard\"", html);
        Assert.DoesNotContain("href=\"/\"", html, StringComparison.Ordinal);
    }
}
