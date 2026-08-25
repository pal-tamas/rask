namespace Rask.Query.Tests;

public class QueryClientTests
{
    private static (QueryClient Client, CountingDispatcher Dispatcher, TestClock Time) NewClient()
    {
        var dispatcher = new CountingDispatcher();
        var time = new TestClock(DateTimeOffset.UnixEpoch);
        return (new QueryClient(dispatcher, time), dispatcher, time);
    }

    private static async Task SettleAsync<T>(Query<T> query)
    {
        // Reading a property is what starts the fetch, exactly as a render would. The entry then
        // exposes the in-flight task so a test can await what a component would simply re-render on.
        _ = query.Data;
        if (query.Entry.InFlight is { } running)
        {
            await running;
        }
    }

    [Fact]
    public async Task A_query_returns_what_the_dispatcher_gave_it()
    {
        var (client, _, _) = NewClient();
        using var query = client.Query(new GetOrders(1));

        await SettleAsync(query);

        Assert.Equal("first", query.Data);
        Assert.False(query.IsLoading);
        Assert.Null(query.Error);
    }

    [Fact]
    public async Task Two_queries_for_the_same_message_share_one_request()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.Block();

        using var a = client.Query(new GetOrders(1));
        using var b = client.Query(new GetOrders(1));

        // Both start while the first is still in flight — which is the case dedup exists for.
        _ = a.Data;
        _ = b.Data;
        dispatcher.Release();
        await (a.Entry.InFlight ?? Task.CompletedTask);

        Assert.Equal(1, dispatcher.QueryCount);
        Assert.Equal("first", b.Data);
    }

    [Fact]
    public async Task The_message_is_the_key_so_a_different_argument_is_a_different_entry()
    {
        var (client, dispatcher, _) = NewClient();

        using var one = client.Query(new GetOrders(1));
        using var two = client.Query(new GetOrders(2));
        await SettleAsync(one);
        await SettleAsync(two);

        Assert.Equal(2, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_fresh_entry_is_served_without_a_second_request()
    {
        var (client, dispatcher, time) = NewClient();
        var options = new QueryOptions { StaleTime = TimeSpan.FromSeconds(30) };

        using var first = client.Query(new GetOrders(1), options);
        await SettleAsync(first);

        time.Advance(TimeSpan.FromSeconds(5));
        using var second = client.Query(new GetOrders(1), options);
        await SettleAsync(second);

        Assert.Equal(1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_second_component_mounting_on_a_stale_entry_refetches()
    {
        var (client, dispatcher, time) = NewClient();
        var options = new QueryOptions { StaleTime = TimeSpan.FromSeconds(30) };

        using var first = client.Query(new GetOrders(1), options);
        await SettleAsync(first);

        time.Advance(TimeSpan.FromSeconds(31));
        dispatcher.Result = "second";

        // Staleness on its own is a condition, not a trigger — a query nobody is looking at does not
        // poll. What refetches it is something starting to observe it, which is TanStack's
        // "refetch on mount" and here is a second component asking for the same data.
        using var second = client.Query(new GetOrders(1), options);
        await SettleAsync(second);

        Assert.Equal(2, dispatcher.QueryCount);
        Assert.Equal("second", second.Data);
    }

    [Fact]
    public async Task A_stale_entry_with_nobody_new_watching_does_not_poll()
    {
        var (client, dispatcher, time) = NewClient();
        var options = new QueryOptions { StaleTime = TimeSpan.FromSeconds(30) };

        using var query = client.Query(new GetOrders(1), options);
        await SettleAsync(query);

        time.Advance(TimeSpan.FromHours(1));

        // Rendering repeatedly must not cost a request each time. This is the regression that made
        // the trigger move out of the property getters: with the default StaleTime of zero, fetching
        // on read is a request per render, for ever.
        _ = query.Data;
        _ = query.Data;
        _ = query.IsLoading;
        await SettleAsync(query);

        Assert.Equal(1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_failed_refetch_keeps_the_data_that_is_already_on_screen()
    {
        var (client, dispatcher, _) = NewClient();
        using var query = client.Query(new GetOrders(1));
        await SettleAsync(query);

        // Blanking a working page because the network blinked is worse than showing data that is a
        // few seconds old with an error beside it.
        dispatcher.Throw = new InvalidOperationException("boom");
        await query.RefetchAsync();

        Assert.Equal("first", query.Data);
        Assert.IsType<InvalidOperationException>(query.Error);
    }

    [Fact]
    public async Task A_command_invalidates_what_it_declares()
    {
        var (client, dispatcher, time) = NewClient();
        var options = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };

        using var orders = client.Query(new GetOrders(1), options);
        await SettleAsync(orders);
        Assert.Equal(1, dispatcher.QueryCount);

        // Set before the mutate: the declared invalidation refetches during MutateAsync, so a value
        // assigned afterwards would arrive too late to be what the refetch saw.
        dispatcher.Result = "after ship";

        // Still well inside the stale window, so only the declared invalidation can cause a refetch.
        time.Advance(TimeSpan.FromSeconds(1));
        await client.MutateAsync(new ShipOrder(7));
        await SettleAsync(orders);

        Assert.Equal(2, dispatcher.QueryCount);
        Assert.Equal("after ship", orders.Data);
    }

    [Fact]
    public async Task A_command_that_declares_nothing_invalidates_nothing()
    {
        var (client, dispatcher, _) = NewClient();
        var options = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };

        using var orders = client.Query(new GetOrders(1), options);
        await SettleAsync(orders);

        await client.MutateAsync(new UnrelatedCommand(7));
        await SettleAsync(orders);

        Assert.Equal(1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task Invalidating_one_query_type_leaves_the_others_alone()
    {
        var (client, dispatcher, _) = NewClient();
        var options = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };

        using var orders = client.Query(new GetOrders(1), options);
        using var profile = client.Query(new GetProfile("ada"), options);
        await SettleAsync(orders);
        await SettleAsync(profile);
        Assert.Equal(2, dispatcher.QueryCount);

        client.Invalidate<GetOrders>();
        await SettleAsync(orders);
        await SettleAsync(profile);

        Assert.Equal(3, dispatcher.QueryCount);
    }

    [Fact]
    public async Task Invalidating_a_type_refreshes_every_page_of_it()
    {
        var (client, dispatcher, _) = NewClient();
        var options = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };

        using var one = client.Query(new GetOrders(1), options);
        using var seven = client.Query(new GetOrders(7), options);
        await SettleAsync(one);
        await SettleAsync(seven);

        // Invalidation is by type, not by exact message: a save should refresh the page the user is
        // not looking at too, or going back to it shows something that was true two screens ago.
        client.Invalidate<GetOrders>();
        await SettleAsync(one);
        await SettleAsync(seven);

        Assert.Equal(4, dispatcher.QueryCount);
    }

    [Fact]
    public async Task SetMessage_re_points_the_query_at_a_new_page()
    {
        var (client, dispatcher, _) = NewClient();
        using var query = client.Query(new GetOrders(1));
        await SettleAsync(query);

        // The defect this exists for: a field initializer runs once, so without re-keying the screen
        // keeps showing page one for ever when the route parameter changes.
        dispatcher.Result = "page two";
        query.SetMessage(new GetOrders(2));
        await SettleAsync(query);

        Assert.Equal("page two", query.Data);
        Assert.Equal(2, dispatcher.QueryCount);
    }

    [Fact]
    public async Task SetMessage_with_the_same_message_is_a_no_op()
    {
        var (client, dispatcher, _) = NewClient();
        var options = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var query = client.Query(new GetOrders(1), options);
        await SettleAsync(query);

        // Calling it unconditionally from OnPropsChanged is the safer habit, so it must be free.
        query.SetMessage(new GetOrders(1));
        await SettleAsync(query);

        Assert.Equal(1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task SetData_fills_the_cache_without_a_request()
    {
        var (client, dispatcher, _) = NewClient();
        var options = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };

        client.SetData(new GetOrders(1), "written");
        using var query = client.Query(new GetOrders(1), options);
        await SettleAsync(query);

        Assert.Equal("written", query.Data);
        Assert.Equal(0, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_disabled_query_does_not_fetch()
    {
        var (client, dispatcher, _) = NewClient();
        using var query = client.Query(new GetOrders(1), new QueryOptions { Enabled = false });

        await SettleAsync(query);

        Assert.Equal(0, dispatcher.QueryCount);
        Assert.True(query.IsLoading);
    }

    [Fact]
    public async Task The_function_form_caches_under_its_own_key()
    {
        var (client, _, _) = NewClient();
        var calls = 0;
        var options = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };

        using var a = client.Query("weather", _ => Task.FromResult(++calls), options);
        await SettleAsync(a);
        using var b = client.Query("weather", _ => Task.FromResult(++calls), options);
        await SettleAsync(b);

        Assert.Equal(1, calls);
        Assert.Equal(1, b.Data);
    }

    [Fact]
    public async Task One_session_cannot_be_served_another_sessions_data()
    {
        // The whole reason the client is registered scoped. Two live sessions are two clients, and a
        // structurally identical message must not cross between them.
        var alice = NewClient();
        var bob = NewClient();

        alice.Dispatcher.Result = "alice's orders";
        bob.Dispatcher.Result = "bob's orders";

        using var forAlice = alice.Client.Query(new GetOrders(1));
        using var forBob = bob.Client.Query(new GetOrders(1));
        await SettleAsync(forAlice);
        await SettleAsync(forBob);

        Assert.Equal("alice's orders", forAlice.Data);
        Assert.Equal("bob's orders", forBob.Data);
        Assert.Equal(1, alice.Dispatcher.QueryCount);
        Assert.Equal(1, bob.Dispatcher.QueryCount);
    }

    [Fact]
    public async Task FetchAsync_throws_what_the_handler_threw()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.Throw = new InvalidOperationException("handler said no");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FetchAsync(new GetOrders(1)));

        Assert.Equal("handler said no", error.Message);
    }
}
