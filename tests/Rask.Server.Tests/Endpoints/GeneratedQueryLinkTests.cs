using System.Net;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

// A link generated without a query argument is the clean address, and the page it lands on shows its
// initializer: `Routes.QueryProbeOrdersPage()` is `/query-probe/orders`, not `/query-probe/orders?page=`.
public partial class GeneratedQueryLinkTests
{
    [Theory]
    [InlineData("plain", "/query-probe/orders", "page-1 per-25")]
    [InlineData("third", "/query-probe/orders?page=3", "page-3 per-25")]
    [InlineData("first", "/query-probe/orders?page=1", "page-1 per-25")]
    [InlineData("sized", "/query-probe/orders?perPage=50", "page-1 per-50")]
    [InlineData("own-url", "/query-probe/orders", "page-1 per-25")]
    public async Task A_generated_link_carries_only_what_it_was_given_and_lands_on_that_page(
        string link, string address, string shown)
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();
        var links = await host.Http.GetStringAsync("/query-probe/links", TestContext.Current.CancellationToken);

        var href = WebUtility.HtmlDecode(Href().Matches(links).Single(m => m.Groups["id"].Value == link).Groups["href"].Value);
        var landed = await host.Http.GetStringAsync(href, TestContext.Current.CancellationToken);

        Assert.Equal(address, href);
        Assert.Contains(shown, landed);
    }

    [Theory]
    [InlineData("go-plain", "/query-probe/orders", "page-1 per-25")]
    [InlineData("go-third", "/query-probe/orders?page=3", "page-3 per-25")]
    public async Task Going_to_a_page_arrives_on_the_address_that_says_only_what_was_given(
        string button, string address, string shown)
    {
        await using var session = await RedirectingSession.Open("/query-probe/links", LiveDiffMode.DisabledFull);

        var frames = await session.Click(button);

        var landing = Assert.Single(frames, f => LiveFrames.HistoryUrl(f) is not null);
        Assert.Equal(address, LiveFrames.HistoryUrl(landing));
        Assert.Contains(shown, landing);
    }

    [GeneratedRegex("""<a id="(?<id>[^"]+)" href="(?<href>[^"]*)">""", RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex Href();
}

[Route("/query-probe/links")]
public sealed partial class QueryProbeLinksPage : Component
{
    protected override Component? Render() =>
        Nav[
            A.Id("plain").Href(Routes.QueryProbeOrdersPage())["orders"],
            A.Id("third").Href(Routes.QueryProbeOrdersPage(Page: 3))["page three"],
            A.Id("first").Href(Routes.QueryProbeOrdersPage(Page: 1))["page one"],
            A.Id("sized").Href(Routes.QueryProbeOrdersPage(PerPage: 50))["fifty a page"],
            A.Id("own-url").Href(QueryProbeOrdersPage.Url())["orders again"],
            Button.Id("go-plain").OnClick(() => { QueryProbeOrdersPage.Go(); })["go to orders"],
            Button.Id("go-third").OnClick(() => { QueryProbeOrdersPage.Go(Page: 3); })["go to page three"]
        ];
}
