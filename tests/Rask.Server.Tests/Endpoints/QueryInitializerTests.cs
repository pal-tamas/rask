using System.Net;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

// `[QueryParam] public int Page { get; set; } = 1;` is the page a list shows without `?page=`: a first
// request and a live navigation both leave the property at its initializer when the address has no value
// the property can hold.
public class QueryInitializerTests
{
    [Theory]
    [InlineData("", "page-1 per-25")]
    [InlineData("?page=3", "page-3 per-25")]
    [InlineData("?page=abc", "page-1 per-25")]
    [InlineData("?page=", "page-1 per-25")]
    [InlineData("?perPage=50", "page-1 per-50")]
    [InlineData("?page=2&perPage=lots", "page-2 per-25")]
    public async Task A_first_request_shows_the_initializer_for_what_the_address_does_not_say(string query, string shown)
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/query-probe/orders" + query, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(shown, body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?page=abc")]
    public async Task A_live_navigation_from_a_value_back_to_none_shows_the_initializer_again(string back)
    {
        await using var fixture = await ConnectedSession.Connect<RoutedTestApp>();
        await fixture.Ws.SendJsonAsync(
            new { type = "navigate", path = "/query-probe/orders", query = "?page=3" }, ct: TestContext.Current.CancellationToken);
        var onPageThree = await fixture.Ws.ReceiveTextAsync();

        await fixture.Ws.SendJsonAsync(
            new { type = "navigate", path = "/query-probe/orders", query = back }, ct: TestContext.Current.CancellationToken);
        var reply = await fixture.Ws.ReceiveTextAsync();

        Assert.Contains("page-3 per-25", onPageThree);
        Assert.Contains("page-1 per-25", reply);
        Assert.DoesNotContain("rask-error-boundary", reply);
    }
}

[Route("/query-probe/orders")]
public sealed partial class QueryProbeOrdersPage : Component
{
    [QueryParam("page")]
    public int Page { get; set; } = 1;

    [QueryParam("perPage")]
    public int? PerPage { get; set; } = 25;

    protected override Component? Render() => Div.Id("orders")[$"page-{Page} per-{PerPage}"];
}
