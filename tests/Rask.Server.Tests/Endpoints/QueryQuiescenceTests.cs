using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Cqrs;
using Rask.Query;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Endpoints;

// Rask.Query starts its fetch inside the client and never returns it to a lifecycle hook, so the
// quiescence pass cannot see it the way it sees an awaited OnMountAsync. Without the hand-over at the
// property read, a query-backed page serves its spinner as the first paint and as the whole document
// a crawler sees — which is the exact problem quiescence exists to solve, unsolved for the framework's
// own way of fetching data.
public class QueryQuiescenceTests
{
    [Fact]
    public async Task Get_WaitsForAQueryThatHasNothingToShow()
    {
        using var host = Host<QueryPageApp>();

        var body = await host.Http.GetStringAsync("/");

        Assert.Contains("orders-loaded", body);
        Assert.DoesNotContain("orders-loading", body);
    }

    [Fact]
    public async Task Get_DoesNotWaitForAQueryThatIsHeldBack()
    {
        // A disabled query is pending but nothing is coming, so there is nothing to wait for. Holding
        // the response for it would spend the whole budget to change nothing — the server-side
        // equivalent of the spinner that turns for ever.
        //
        // This is the reachable half of "only wait when something is actually in flight". The other
        // half — data present while a stale entry revalidates — cannot be exercised on an initial GET
        // at all: IQueryClient is scoped per session, so every first paint starts with a cold cache
        // and that case only arises after a navigation within a live session.
        using var host = Host<DisabledQueryApp>();

        var started = DateTime.UtcNow;
        var body = await host.Http.GetStringAsync("/");
        var elapsed = DateTime.UtcNow - started;

        Assert.Contains("not-enabled", body);
        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"took {elapsed}; it waited for a query held back");
    }

    // QueryClient takes an IDispatcher even for the raw-fetch overloads these tests use, so CQRS is
    // registered for its wiring rather than because a message crosses it.
    private static RaskTestHost Host<TApp>() where TApp : Component =>
        RaskTestHost.Create<TApp>(configureServices: s =>
        {
            s.AddRaskCqrs();
            s.AddRaskQuery();
        });
}

public sealed partial class QueryPageApp : Component
{
    private readonly Query<string> _orders;

    public QueryPageApp(IQueryClient client) =>
        _orders = client.Query<string>("orders", async ct =>
        {
            await Task.Delay(30, ct);
            return "orders-loaded";
        });

    protected override Component? HeadAssets => Title["query-page"];

    protected override Component? Render() =>
        Div[_orders.IsLoading ? "orders-loading" : _orders.Data ?? "no-data"];
}

public sealed partial class DisabledQueryApp : Component
{
    private readonly Query<string> _value;

    public DisabledQueryApp(IQueryClient client) =>
        _value = client.Query<string>(
            "held-back",
            async ct =>
            {
                await Task.Delay(30, ct);
                return "should-never-run";
            },
            new QueryOptions { Enabled = false });

    protected override Component? HeadAssets => Title["disabled-query"];

    protected override Component? Render() => Div[_value.Data ?? "not-enabled"];
}
