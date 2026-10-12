using System.Net;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

// A filter the page cannot read is no filter: the page answers as it does without it. A mailed link
// with a stale or hand-edited value used to be a RouteBindException, the error page and a 500.
public class UnreadableQueryTests
{
    [Theory]
    [InlineData("?weightFrom=abc")]
    [InlineData("?weightFrom=1.5")]
    [InlineData("?weightFrom=99999999999999999999")]
    [InlineData("?weightFrom=")]
    [InlineData("?page=two")]
    [InlineData("?weightFrom=abc&weightFrom=5")]
    public async Task A_query_value_its_page_cannot_read_answers_200_with_the_filter_not_applied(string query)
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/query-probe/parcels" + query, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("weight-any page-0", body);
        Assert.DoesNotContain("rask-error-boundary", body);
    }

    [Fact]
    public async Task The_readable_parameters_beside_an_unreadable_one_are_applied()
    {
        using var host = RaskTestHost.Create<RoutedTestApp>();

        var response = await host.Http.GetAsync("/query-probe/parcels?weightFrom=12&page=two", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("weight-12 page-0", body);
    }

    [Fact]
    public async Task A_live_navigation_with_an_unreadable_query_value_shows_the_page_without_the_filter()
    {
        await using var fixture = await ConnectedSession.Connect<RoutedTestApp>();

        await fixture.Ws.SendJsonAsync(
            new { type = "navigate", path = "/query-probe/parcels", query = "?weightFrom=abc&page=3" }, ct: TestContext.Current.CancellationToken);
        var reply = await fixture.Ws.ReceiveTextAsync();

        Assert.Contains("weight-any page-3", reply);
        Assert.Contains("/query-probe/parcels?weightFrom=abc&page=3", reply.Replace("\\u0026", "&", StringComparison.Ordinal));
        Assert.DoesNotContain("rask-error-boundary", reply);
    }
}

[Route("/query-probe/parcels")]
public sealed partial class QueryProbeParcelsPage : Component
{
    [QueryParam("weightFrom")]
    public int? WeightFrom { get; set; }

    [QueryParam("page")]
    public int Page { get; set; }

    protected override Component? Render() =>
        Div.Id("parcels")[$"weight-{WeightFrom?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "any"} page-{Page}"];
}
