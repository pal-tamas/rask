using Rask.Cqrs;

namespace Rask.Query.Tests;

public class RetryAndPollingTests
{
    /// <summary>No waiting: the backoff is TanStack's, and asserting it in real time would cost seven
    /// seconds a test to prove arithmetic.</summary>
    private static QueryOptions Instant(int retry) => new()
    {
        Retry = retry,
        RetryDelay = _ => TimeSpan.Zero,
        StaleTime = TimeSpan.FromHours(1),
    };

    private static (QueryClient Client, CountingDispatcher Dispatcher) NewClient()
    {
        var dispatcher = new CountingDispatcher();
        return (new QueryClient(dispatcher, new TestClock(DateTimeOffset.UnixEpoch)), dispatcher);
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
    public async Task A_failing_query_is_retried_up_to_the_configured_count()
    {
        var (client, dispatcher) = NewClient();
        dispatcher.Throw = new InvalidOperationException("boom");

        using var query = client.Query(new GetOrders(1), Instant(retry: 2));
        await Settle(query);

        // One attempt plus two retries. The count is what proves the loop is bounded — the shape
        // this replaced could retry through the notification path for ever.
        Assert.Equal(3, dispatcher.QueryCountFor<GetOrders>());
        Assert.Equal(QueryStatus.Error, query.Status);
    }

    [Fact]
    public async Task A_retry_that_succeeds_clears_the_error()
    {
        var (client, dispatcher) = NewClient();
        dispatcher.FailTimes = 2;

        using var query = client.Query(new GetOrders(1), Instant(retry: 3));
        await Settle(query);

        Assert.Equal(3, dispatcher.QueryCountFor<GetOrders>());
        Assert.Equal(QueryStatus.Success, query.Status);
        Assert.Equal("first", query.Data);
        Assert.Null(query.Error);
    }

    [Fact]
    public async Task A_refused_request_is_not_retried()
    {
        var (client, dispatcher) = NewClient();
        dispatcher.Throw = new RemoteDispatchException("forbidden") { StatusCode = 403 };

        using var query = client.Query(new GetOrders(1), Instant(retry: 3));
        await Settle(query);

        // A 403 will never succeed on a retry. Retrying it turns one refused request into four and
        // delays telling the user anything by several seconds.
        Assert.Equal(1, dispatcher.QueryCountFor<GetOrders>());
        Assert.Equal(QueryStatus.Error, query.Status);
    }

    [Fact]
    public async Task A_server_error_is_retried()
    {
        var (client, dispatcher) = NewClient();
        dispatcher.Throw = new RemoteDispatchException("unwell") { StatusCode = 503 };

        using var query = client.Query(new GetOrders(1), Instant(retry: 2));
        await Settle(query);

        // 5xx is exactly the transient case retry exists for.
        Assert.Equal(3, dispatcher.QueryCountFor<GetOrders>());
    }

    [Fact]
    public async Task Retry_can_be_turned_off()
    {
        var (client, dispatcher) = NewClient();
        dispatcher.Throw = new InvalidOperationException("boom");

        using var query = client.Query(new GetOrders(1), Instant(retry: 0));
        await Settle(query);

        Assert.Equal(1, dispatcher.QueryCountFor<GetOrders>());
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1, 2000)]
    [InlineData(2, 4000)]
    [InlineData(20, 30000)]
    public void The_default_backoff_doubles_and_is_capped(int attempt, int expectedMs) =>
        Assert.Equal(expectedMs, (int)QueryOptions.DefaultRetryDelay(attempt).TotalMilliseconds);

    [Fact]
    public void A_cancellation_is_never_worth_retrying() =>
        Assert.False(QueryOptions.IsWorthRetrying(new OperationCanceledException()));

    // ---------------------------------------------------------------- polling

    [Fact]
    public async Task A_polling_query_refetches_on_its_interval()
    {
        var (client, dispatcher) = NewClient();
        var options = new QueryOptions
        {
            StaleTime = TimeSpan.FromHours(1),
            RefetchInterval = TimeSpan.FromMilliseconds(15),
        };

        using var query = client.Query(new GetOrders(1), options);
        await Settle(query);

        // Real time, deliberately: the loop is a Task.Delay, which is what the rest of the repo uses,
        // and a fake clock would not drive it. Bounded so a stall fails rather than hangs.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (dispatcher.QueryCountFor<GetOrders>() < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(
            dispatcher.QueryCountFor<GetOrders>() >= 3,
            $"expected the poll to refetch; saw {dispatcher.QueryCountFor<GetOrders>()} fetches");
    }

    [Fact]
    public async Task Disposing_a_polling_query_stops_it()
    {
        var (client, dispatcher) = NewClient();
        var options = new QueryOptions
        {
            StaleTime = TimeSpan.FromHours(1),
            RefetchInterval = TimeSpan.FromMilliseconds(15),
        };

        var query = client.Query(new GetOrders(1), options);
        await Settle(query);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (dispatcher.QueryCountFor<GetOrders>() < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        query.Dispose();
        var afterDispose = dispatcher.QueryCountFor<GetOrders>();

        // A polling query that outlives its component keeps a session doing work for ever.
        await Task.Delay(120);
        Assert.Equal(afterDispose, dispatcher.QueryCountFor<GetOrders>());
    }

    [Fact]
    public async Task A_query_with_no_interval_never_polls()
    {
        var (client, dispatcher) = NewClient();
        using var query = client.Query(new GetOrders(1), new QueryOptions { StaleTime = TimeSpan.FromHours(1) });
        await Settle(query);

        await Task.Delay(80);

        Assert.Equal(1, dispatcher.QueryCountFor<GetOrders>());
    }
}
