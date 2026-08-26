namespace Rask.Query.Tests;

/// <summary>
///     What happens to a fetch, and to a cached entry, when the thing that wanted it goes away.
/// </summary>
public class QueryLifetimeTests
{
    private static (QueryClient Client, CountingDispatcher Dispatcher, TestClock Time) NewClient()
    {
        var dispatcher = new CountingDispatcher();
        var time = new TestClock(DateTimeOffset.UnixEpoch);
        return (new QueryClient(dispatcher, time), dispatcher, time);
    }

    [Fact]
    public async Task Losing_the_last_observer_cancels_the_fetch_it_started()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.Block();

        var query = client.Query(new GetOrders(1));
        _ = query.Data;

        var inFlight = query.Entry.InFlight;
        Assert.NotNull(inFlight);

        // The component unmounted. Whatever is in flight is work for a screen that has gone —
        // leaving it running means a navigation costs a database round trip nobody will read.
        var entry = query.Entry;
        query.Dispose();
        await inFlight;

        Assert.Null(entry.InFlight);
        Assert.False(entry.HasData);

        // Cancellation is not a failure. Recording it would leave an error on an entry whose next
        // observer renders it, having done nothing wrong.
        Assert.Null(entry.Error);
    }

    [Fact]
    public async Task A_cancelled_fetch_leaves_the_work_owed_for_the_next_observer()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.Block();

        var first = client.Query(new GetOrders(1));
        _ = first.Data;
        var inFlight = first.Entry.InFlight!;
        first.Dispose();
        await inFlight;

        // Nothing was retrieved, so the next component to want this data must actually fetch it
        // rather than inherit an entry that believes it is up to date.
        dispatcher.Release();
        using var second = client.Query(new GetOrders(1));
        _ = second.Data;
        if (second.Entry.InFlight is { } running)
        {
            await running;
        }

        Assert.Equal("first", second.Data);
    }

    [Fact]
    public async Task A_custom_GcTime_outlives_the_default()
    {
        var (client, dispatcher, time) = NewClient();
        var keep = new QueryOptions { GcTime = TimeSpan.FromHours(1), StaleTime = TimeSpan.FromHours(1) };

        var orders = client.Query(new GetOrders(1), keep);
        await Settle(orders);
        orders.Dispose();

        // Well past the five-minute default, well inside the hour this query asked for.
        time.Advance(TimeSpan.FromMinutes(30));

        // Collection runs when something detaches, so give it an unrelated query to detach.
        client.Query(new GetProfile("ada"), keep).Dispose();

        // The entry survived, so this is served from cache. Reading GcTime from a single default at
        // collection time — the bug this covers — would have dropped it at five minutes and made
        // this a second round trip.
        using var again = client.Query(new GetOrders(1), keep);
        await Settle(again);

        Assert.Equal(1, dispatcher.QueryCountFor<GetOrders>());
    }

    [Fact]
    public async Task Two_queries_sharing_a_key_keep_the_longest_lifetime_asked_for()
    {
        var (client, dispatcher, time) = NewClient();
        var brief = new QueryOptions { GcTime = TimeSpan.FromSeconds(1), StaleTime = TimeSpan.FromHours(1) };
        var long_ = new QueryOptions { GcTime = TimeSpan.FromHours(1), StaleTime = TimeSpan.FromHours(1) };

        var a = client.Query(new GetOrders(1), brief);
        var b = client.Query(new GetOrders(1), long_);
        await Settle(a);
        a.Dispose();
        b.Dispose();

        time.Advance(TimeSpan.FromMinutes(30));
        client.Query(new GetProfile("ada"), brief).Dispose();

        // The entry has to outlive whichever observer needs it most; taking the shorter would drop
        // data the other one is entitled to find still there.
        using var again = client.Query(new GetOrders(1), long_);
        await Settle(again);

        Assert.Equal(1, dispatcher.QueryCountFor<GetOrders>());
    }

    private static async Task Settle<T>(Query<T> query)
    {
        _ = query.Data;
        if (query.Entry.InFlight is { } running)
        {
            await running;
        }
    }
}
