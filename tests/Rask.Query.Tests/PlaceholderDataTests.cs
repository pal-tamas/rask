namespace Rask.Query.Tests;

/// <summary>
///     Paging without the table blinking. This is the difference between a page change that feels
///     instant and one that replaces the content with a spinner and back again.
/// </summary>
public class PlaceholderDataTests
{
    private static (QueryClient Client, CountingDispatcher Dispatcher) NewClient()
    {
        var dispatcher = new CountingDispatcher();
        return (new QueryClient(dispatcher, new TestClock(DateTimeOffset.UnixEpoch)), dispatcher);
    }

    private static QueryOptions Keeping => new()
    {
        KeepPreviousData = true,
        StaleTime = TimeSpan.FromHours(1),
    };

    private static async Task Settle<T>(Query<T> query)
    {
        _ = query.Data;
        if (query.Entry.InFlight is { } running)
        {
            await running;
        }
    }

    [Fact]
    public async Task The_previous_page_stays_on_screen_while_the_next_one_loads()
    {
        var (client, dispatcher) = NewClient();
        using var query = client.Query(new GetOrders(1), Keeping);
        await Settle(query);
        Assert.Equal("first", query.Data);

        dispatcher.Block();
        dispatcher.Result = "page two";
        query.SetMessage(new GetOrders(2));

        // Page one's rows are still there, and the query reports success rather than pending — so a
        // component renders the table it already has instead of a spinner over it.
        Assert.Equal("first", query.Data);
        Assert.True(query.IsPlaceholderData);
        Assert.Equal(QueryStatus.Success, query.Status);
        Assert.True(query.IsFetching);

        dispatcher.Release();
        await Settle(query);

        Assert.Equal("page two", query.Data);
        Assert.False(query.IsPlaceholderData);
    }

    [Fact]
    public async Task Without_the_option_a_re_keyed_query_goes_blank()
    {
        var (client, dispatcher) = NewClient();
        var plain = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var query = client.Query(new GetOrders(1), plain);
        await Settle(query);

        dispatcher.Block();
        query.SetMessage(new GetOrders(2));

        // The default: nothing to show, which is what a spinner is for.
        Assert.Null(query.Data);
        Assert.False(query.IsPlaceholderData);
        Assert.Equal(QueryStatus.Pending, query.Status);
        Assert.True(query.IsLoading);

        dispatcher.Release();
        await Settle(query);
    }

    [Fact]
    public async Task Navigating_to_a_page_already_cached_shows_that_page_not_the_previous_one()
    {
        var (client, dispatcher) = NewClient();

        // Warm page two, then go 1 -> 2. The result is set BEFORE each query is created: the fetch
        // starts in the constructor, so assigning it afterwards would arrive too late.
        dispatcher.Result = "page two";
        using var warm = client.Query(new GetOrders(2), Keeping);
        await Settle(warm);

        dispatcher.Result = "page one";
        using var query = client.Query(new GetOrders(1), Keeping);
        await Settle(query);
        Assert.Equal("page one", query.Data);

        dispatcher.Block();
        query.SetMessage(new GetOrders(2));

        // The placeholder is only for a key with nothing behind it. Showing page one here would be a
        // step backwards from data already in hand.
        Assert.Equal("page two", query.Data);
        Assert.False(query.IsPlaceholderData);

        dispatcher.Release();
    }

    [Fact]
    public async Task A_prefetch_makes_the_next_page_instant()
    {
        var (client, dispatcher) = NewClient();
        var keep = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };

        await client.PrefetchAsync(new GetOrders(2), keep);
        Assert.Equal(1, dispatcher.QueryCountFor<GetOrders>());

        using var query = client.Query(new GetOrders(2), keep);
        await Settle(query);

        // Served from the warmed entry: no second round trip, and no pending frame.
        Assert.Equal("first", query.Data);
        Assert.Equal(1, dispatcher.QueryCountFor<GetOrders>());
    }

    [Fact]
    public async Task A_failed_prefetch_does_not_throw()
    {
        var (client, dispatcher) = NewClient();
        dispatcher.Throw = new InvalidOperationException("boom");

        // A prefetch is a guess about where the user is going. A wrong guess must not surface as a
        // failure at the navigation that made it.
        await client.PrefetchAsync(new GetOrders(2), new QueryOptions { Retry = 0 });
    }
}
