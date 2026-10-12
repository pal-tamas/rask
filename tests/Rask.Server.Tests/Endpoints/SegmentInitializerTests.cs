using System.Net;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

// `[Route("/items/{count?}")]` with `[RouteParam] public int Count { get; set; } = 5;`: the segment left out
// leaves the property at its initializer, on a first request and on a live navigation — the rule a query
// parameter has. A segment the page cannot read is still not that route's.
public class SegmentInitializerTests
{
    [Theory]
    [InlineData("", "count-5")]
    [InlineData("/7", "count-7")]
    [InlineData("/0", "count-0")]
    public async Task A_first_request_shows_the_initializer_for_a_segment_the_address_leaves_out(string segment, string shown)
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/segment-probe/items" + segment, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(shown, body);
    }

    [Fact]
    public async Task A_live_navigation_from_a_segment_back_to_none_shows_the_initializer_again()
    {
        await using var fixture = await ConnectedSession.Connect<RoutedTestApp>();
        await fixture.Ws.SendJsonAsync(
            new { type = "navigate", path = "/segment-probe/items/7", query = "" }, ct: TestContext.Current.CancellationToken);
        var onSeven = await fixture.Ws.ReceiveTextAsync();

        await fixture.Ws.SendJsonAsync(
            new { type = "navigate", path = "/segment-probe/items", query = "" }, ct: TestContext.Current.CancellationToken);
        var reply = await fixture.Ws.ReceiveTextAsync();

        Assert.Contains("count-7", onSeven);
        Assert.Contains("count-5", reply);
        Assert.DoesNotContain("rask-error-boundary", reply);
    }

    [Fact]
    public async Task A_segment_the_page_cannot_read_is_still_not_found()
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/segment-probe/items/many", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

[Route("/segment-probe/items/{count?}")]
public sealed partial class SegmentProbeItemsPage : Component
{
    [RouteParam]
    public int Count { get; set; } = 5;

    protected override Component? Render() => Div.Id("items")[$"count-{Count}"];
}
