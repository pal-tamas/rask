using System.Text.RegularExpressions;
using Rask.Site.Features.UiDocs;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

// The site is a static publish, so a link to a parameterised page lands only if the prerender pass was told the path.
public sealed class AppFrameDemoPrerenderPathsTests
{
    [Fact]
    public async Task Every_driver_the_drivers_list_links_is_a_page_the_publish_writes()
    {
        var html = await SiteDocument.At((string)global::Rask.Site.Routes.AppFrameDriversDemoPage());

        var linked = Regex.Matches(html, "href=\"(/demo/app-frame/drivers/[^\"]+)\"").Select(match => match.Groups[1].Value).ToList();
        var written = new AppFrameDemoPrerenderPaths().Paths().ToList();

        Assert.Equal(AppFrameDemoDrivers.All.Count, linked.Count);
        Assert.All(linked, href => Assert.Contains(href, written));
    }
}
