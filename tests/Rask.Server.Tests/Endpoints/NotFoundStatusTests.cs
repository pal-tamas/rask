using System.Net;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Endpoints;

// A path that falls through to the not-found page used to answer 200. That page renders perfectly
// ordinary HTML, so nothing downstream could tell it from a real one — caches stored it, crawlers
// indexed it, uptime checks reported green. Exactly the defect #607 fixed for a crashed page.
public class NotFoundStatusTests
{
    [Fact]
    public async Task Get_PathThatFallsThrough_Answers404()
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/no-such-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_PathThatFallsThrough_StillRendersTheNotFoundPage()
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/no-such-page");

        // Only the status changes. The page still renders and the live session still attaches, so
        // the reload button and navigating away both keep working.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-rask-root=\"", body);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Get_MatchedRoute_StaysOk()
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/ssr-404-probe/known");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_AppThatDoesNotRoute_StaysOk()
    {
        // The route table resolves the fallback for EVERY app, because BuildTree always registers
        // it. An app whose root renders directly mounts no Router, so it never shows that page —
        // and 404-ing every path such an app serves would be a far worse lie than the one being
        // fixed. This is why the status is confirmed against what the render mounted.
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/anything-at-all");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

// Routes through a Router, so a miss genuinely lands the user on the not-found page.
public sealed partial class RoutedTestApp : Component
{
    protected override Component? HeadAssets => Title["ssr-404"];

    protected override Component? Render() => Router;
}

[Route("/ssr-404-probe/known")]
public sealed partial class Ssr404KnownPage : Component
{
    protected override Component? Render() => Div.Id("known")["known-content"];
}
