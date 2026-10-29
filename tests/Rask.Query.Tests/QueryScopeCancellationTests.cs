using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Cqrs;
using Rask.Testing;

namespace Rask.Querying.Tests;

/// <summary>
///     A fetch stops when what owns it goes away — the session, or every component reading it — and stopping is
///     never an error on the entry.
/// </summary>
public class QueryScopeCancellationTests
{
    private static readonly QueryOptions Fresh = new() { StaleTime = TimeSpan.FromHours(1) };

    [Fact]
    public async Task Ending_the_session_cancels_a_fetch_its_query_still_watches()
    {
        var dispatcher = new CountingDispatcher();
        var client = new SessionQueryClient(dispatcher);
        dispatcher.Block();
        var query = client.Query(new GetOrders(1));
        _ = query.Data;
        var inFlight = query.Entry.InFlight!;

        client.Stop();
        await inFlight.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, dispatcher.Cancelled);
        Assert.Null(query.Entry.InFlight);
        Assert.Null(query.Error);
        Assert.Equal(QueryStatus.Pending, query.Status);
    }

    [Fact]
    public async Task A_query_polled_after_its_session_ended_asks_for_nothing_more()
    {
        var dispatcher = new CountingDispatcher();
        var client = new SessionQueryClient(dispatcher);
        var polled = new QueryOptions { RefetchInterval = TimeSpan.FromMilliseconds(10) };
        using var query = client.Query(new GetOrders(1), polled);
        await Until(() => dispatcher.QueryCount >= 2);

        client.Stop();
        var stoppedAt = dispatcher.QueryCount;
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // One tick may already have been past its check as the session stopped; none after it.
        Assert.InRange(dispatcher.QueryCount, stoppedAt, stoppedAt + 1);
    }

    [Fact]
    public async Task Unmounting_every_reader_cancels_their_fetch_without_an_error()
    {
        var (services, dispatcher) = Session();
        dispatcher.Block();
        var holder = new Holder();
        var rendered = Page.Render(holder, services);
        rendered.Shows("a:loading");

        holder.Hide(a: true, b: true);

        rendered.DoesNotShow("a:");
        await Until(() => dispatcher.Cancelled == 1);
        Assert.Null(holder.Orders!.Entry.Error);
        Assert.Null(holder.Orders.Entry.InFlight);
    }

    [Fact]
    public void Unmounting_one_reader_of_a_shared_query_leaves_the_fetch_for_the_other()
    {
        var (services, dispatcher) = Session();
        dispatcher.Block();
        var holder = new Holder();
        var rendered = Page.Render(holder, services);
        rendered.Shows("b:loading");

        holder.Hide(a: true, b: false);
        rendered.DoesNotShow("a:");
        dispatcher.Release();

        rendered.Shows("b:first");
        Assert.Equal(0, dispatcher.Cancelled);
        Assert.Equal(1, dispatcher.QueryCount);
    }

    private static (ServiceProvider Services, CountingDispatcher Dispatcher) Session()
    {
        var dispatcher = new CountingDispatcher();
        var services = new ServiceCollection()
            .AddSingleton<IDispatcher>(dispatcher)
            .AddRaskQuery()
            .BuildServiceProvider();
        return (services, dispatcher);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    /// <summary>Asks for the orders without reading them, for two children that do.</summary>
    private sealed class Holder : Component
    {
        private readonly Reader _a;
        private readonly Reader _b;
        private bool _showA = true;
        private bool _showB = true;

        public Holder()
        {
            _a = new Reader("a", () => Orders!);
            _b = new Reader("b", () => Orders!);
        }

        public Query<string>? Orders { get; private set; }

        public void Hide(bool a, bool b)
        {
            _showA = !a;
            _showB = !b;
            StateHasChanged();
        }

        protected override Component? Render()
        {
            Orders = QueryClient.Query(new GetOrders(1), Fresh);
            return Div[_showA ? _a : null, _showB ? _b : null];
        }
    }

    private sealed class Reader(string name, Func<Query<string>> orders) : Component
    {
        protected override Component? Render() => P[name + ":" + (orders().Data ?? "loading")];
    }
}
