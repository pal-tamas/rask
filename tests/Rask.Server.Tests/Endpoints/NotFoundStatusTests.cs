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
    public async Task A_path_that_falls_through_answers_404()
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/no-such-page", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_path_that_falls_through_still_renders_the_not_found_page()
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/no-such-page", TestContext.Current.CancellationToken);

        // Only the status changes. The page still renders and the live session still attaches, so
        // the reload button and navigating away both keep working.
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("data-rask-root=\"", body);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task A_matched_route_stays_200()
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/ssr-404-probe/known", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/ssr-404-probe/items/99999999999999999999")]
    [InlineData("/ssr-404-probe/items/seven")]
    [InlineData("/ssr-404-probe/days/2026-13-45")]
    public async Task A_segment_its_route_cannot_read_answers_404_with_the_not_found_page(string path)
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync(path, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("data-rask-root=\"", body);
        Assert.DoesNotContain("rask-error-boundary", body);
    }

    [Theory]
    [InlineData("/ssr-404-probe/items/007", "item-7")]
    [InlineData("/ssr-404-probe/days/2026-10-12", "day-2026-10-12")]
    public async Task A_segment_its_route_can_read_stays_200_and_is_bound(string path, string shown)
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync(path, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(shown, body);
    }

    [Fact]
    public async Task A_live_navigation_to_a_segment_its_route_cannot_read_shows_the_not_found_page()
    {
        await using var fixture = await ConnectedSession.Connect<RoutedTestApp>();

        await fixture.Ws.SendJsonAsync(
            new { type = "navigate", path = "/ssr-404-probe/items/99999999999999999999", query = "" }, ct: TestContext.Current.CancellationToken);
        var reply = await fixture.Ws.ReceiveTextAsync();

        Assert.Contains("/ssr-404-probe/items/99999999999999999999", reply);
        Assert.DoesNotContain("rask-error-boundary", reply);
        Assert.DoesNotContain("item-", reply);
    }

    [Fact]
    public async Task An_app_that_does_not_route_stays_200()
    {
        // The route table resolves the fallback for EVERY app, because BuildTree always registers
        // it. An app whose root renders directly mounts no Router, so it never shows that page —
        // and 404-ing every path such an app serves would be a far worse lie than the one being
        // fixed. This is why the status is confirmed against what the render mounted.
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/anything-at-all", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

// Routes through a Router, so a miss genuinely lands the user on the not-found page.
public sealed partial class RoutedTestApp : Component
{
    protected override Component? HeadAssets => Title["ssr-404"];

    protected override Component? Render() => Router;
}

[Route("/ssr-404-probe/items/{id:int}")]
public sealed partial class Ssr404ItemPage : Component
{
    [RouteParam]
    public int Id { get; set; }

    protected override Component? Render() => Div.Id("item")[$"item-{Id}"];
}

[Route("/ssr-404-probe/days/{day}")]
public sealed partial class Ssr404DayPage : Component
{
    [RouteParam]
    public DateOnly Day { get; set; }

    protected override Component? Render() => Div.Id("day")[$"day-{Day:O}"];
}

[Route("/ssr-404-probe/known")]
public sealed partial class Ssr404KnownPage : Component
{
    protected override Component? Render() => Div.Id("known")["known-content"];
}
