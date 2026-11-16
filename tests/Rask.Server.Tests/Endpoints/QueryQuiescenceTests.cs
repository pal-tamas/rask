using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Cqrs;
using Rask.Querying;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Endpoints;

// Rask.Query starts its fetch inside the client and never returns it to a lifecycle hook, so the
// quiescence pass cannot see it the way it sees an awaited Mount. Without the hand-over at the
// property read, a query-backed page serves its spinner as the first paint and as the whole document
// a crawler sees — which is the exact problem quiescence exists to solve, unsolved for the framework's
// own way of fetching data.
public class QueryQuiescenceTests
{
    [Fact]
    public async Task The_first_response_waits_for_a_query_that_has_nothing_to_show()
    {
        using var host = Host<QueryPageApp>();

        var body = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Contains("orders-loaded", body);
        Assert.DoesNotContain("orders-loading", body);
    }

    [Fact]
    public async Task The_first_response_does_not_wait_for_a_query_that_is_held_back()
    {
        // A disabled query is pending but nothing is coming, so there is nothing to wait for. Holding
        // the response for it would spend the whole budget to change nothing — the server-side
        // equivalent of the spinner that turns for ever.
        //
        // This is the reachable half of "only wait when something is actually in flight". The other
        // half — data present while a stale entry revalidates — cannot be exercised on an initial GET
        // at all: IQueryClient is scoped per session, so every first paint starts with a cold cache
        // and that case only arises after a navigation within a live session.
        //
        // Proven by order, not by a clock: the budget is set far beyond the test, so a response that arrives at
        // all was not held for anything, and the fetch is gated, so that a server which started it to wait for
        // it is caught at the start and fails the test at once.
        var heldBack = new HeldBackFetch();
        using var host = RaskTestHost.Create<DisabledQueryApp>(
            configureServices: s =>
            {
                s.AddRaskCqrs();
                s.AddRaskQuery();
                s.AddSingleton(heldBack);
            },
            configureServer: o => o.QuiescenceTimeout = TimeSpan.FromHours(1));

        var response = host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var first = await Task.WhenAny(response, heldBack.Started.Task);

        Assert.Same(response, first);
        Assert.Contains("not-enabled", await response);
        Assert.False(heldBack.Started.Task.IsCompleted, "the held-back query was fetched");
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

/// <summary>The held-back query's fetch: it says when it starts, and never finishes on its own.</summary>
public sealed class HeldBackFetch
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<string> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed partial class DisabledQueryApp : Component
{
    private readonly Query<string> _value;

    public DisabledQueryApp(IQueryClient client, HeldBackFetch fetch) =>
        _value = client.Query<string>(
            "held-back",
            async ct =>
            {
                fetch.Started.TrySetResult();
                return await fetch.Gate.Task.WaitAsync(ct);
            },
            new QueryOptions { Enabled = false });

    protected override Component? HeadAssets => Title["disabled-query"];

    protected override Component? Render() => Div[_value.Data ?? "not-enabled"];
}
