namespace Rask.Query.Tests;

/// <summary>
///     <see cref="QueryStatus" /> and <see cref="FetchStatus" /> are orthogonal, and these are the
///     combinations that prove it — the ones a single enum could not express.
/// </summary>
public class QueryStatusTests
{
    private static (QueryClient Client, CountingDispatcher Dispatcher, TestClock Time) NewClient()
    {
        var dispatcher = new CountingDispatcher();
        var time = new TestClock(DateTimeOffset.UnixEpoch);
        return (new QueryClient(dispatcher, time), dispatcher, time);
    }

    private static async Task Settle<T>(Query<T> query)
    {
        _ = query.Data;
        if (query.Entry.InFlight is { } running)
        {
            await running;
        }
    }

    [Fact]
    public async Task A_first_load_is_pending_and_fetching()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.Block();

        using var query = client.Query(new GetOrders(1));
        _ = query.Data;

        Assert.Equal(QueryStatus.Pending, query.Status);
        Assert.Equal(FetchStatus.Fetching, query.FetchStatus);

        // The one state that warrants a spinner: nothing to show, and something coming.
        Assert.True(query.IsLoading);

        dispatcher.Release();
        await (query.Entry.InFlight ?? Task.CompletedTask);
    }

    [Fact]
    public async Task A_refresh_in_place_is_successful_and_fetching_at_once()
    {
        var (client, dispatcher, _) = NewClient();
        using var query = client.Query(new GetOrders(1));
        await Settle(query);

        dispatcher.Block();
        client.Invalidate<GetOrders>();

        // This is the pair a single enum cannot express, and the reason there are two. Rendering a
        // spinner here would hide data the user already has.
        Assert.Equal(QueryStatus.Success, query.Status);
        Assert.Equal(FetchStatus.Fetching, query.FetchStatus);
        Assert.False(query.IsLoading);
        Assert.True(query.IsFetching);
        Assert.Equal("first", query.Data);

        dispatcher.Release();
        await (query.Entry.InFlight ?? Task.CompletedTask);
    }

    [Fact]
    public async Task A_settled_query_is_successful_and_idle()
    {
        var (client, _, _) = NewClient();
        using var query = client.Query(new GetOrders(1));
        await Settle(query);

        Assert.Equal(QueryStatus.Success, query.Status);
        Assert.Equal(FetchStatus.Idle, query.FetchStatus);
        Assert.True(query.IsSuccess);
        Assert.False(query.IsError);
    }

    [Fact]
    public async Task A_failed_refresh_reports_error_while_still_holding_the_data()
    {
        var (client, dispatcher, _) = NewClient();
        using var query = client.Query(new GetOrders(1), new QueryOptions { Retry = 0 });
        await Settle(query);

        dispatcher.Throw = new InvalidOperationException("boom");
        await query.RefetchAsync();

        // Error and data together: the component renders the rows it has with a message beside
        // them, rather than a blank page because the network blinked.
        Assert.Equal(QueryStatus.Error, query.Status);
        Assert.Equal(FetchStatus.Idle, query.FetchStatus);
        Assert.True(query.IsError);
        Assert.Equal("first", query.Data);
    }
}
