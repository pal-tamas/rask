namespace Rask.Query.Tests;

/// <summary>
///     Invalidation through the real cache: what a prefix reaches, and what it must not.
/// </summary>
public class QueryHierarchyTests
{
    private static (QueryClient Client, CountingDispatcher Dispatcher) NewClient()
    {
        var dispatcher = new CountingDispatcher();
        return (new QueryClient(dispatcher, new TestClock(DateTimeOffset.UnixEpoch)), dispatcher);
    }

    private static async Task SettleAsync<T>(Query<T> query)
    {
        _ = query.Data;
        if (query.Entry.InFlight is { } running)
        {
            await running;
        }
    }

    [Fact]
    public async Task Invalidating_a_message_type_still_refetches_every_one_of_its_entries()
    {
        // THE behaviour this change must not alter. It used to be a Group-equality special case and is
        // now a prefix match, and the observable result has to be identical: a save that affects orders
        // refreshes page one and page seven alike, not whichever the caller happened to hold.
        var (client, dispatcher) = NewClient();
        using var first = client.Query(new GetOrders(1));
        using var seventh = client.Query(new GetOrders(7));
        await SettleAsync(first);
        await SettleAsync(seventh);

        var before = dispatcher.QueryCount;
        client.Invalidate<GetOrders>();
        await SettleAsync(first);
        await SettleAsync(seventh);

        Assert.Equal(before + 2, dispatcher.QueryCount);
    }

    [Fact]
    public async Task Invalidating_one_message_type_leaves_another_alone()
    {
        var (client, dispatcher) = NewClient();
        using var orders = client.Query(new GetOrders(1));
        using var profile = client.Query(new GetProfile("ada"));
        await SettleAsync(orders);
        await SettleAsync(profile);

        var before = dispatcher.QueryCount;
        client.Invalidate<GetOrders>();
        await SettleAsync(orders);
        await SettleAsync(profile);

        Assert.Equal(before + 1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_prefix_reaches_every_query_written_beneath_it()
    {
        // The thing a flat key could not express: one call refreshing a list and a detail that are
        // different message types, because the app said they belong together.
        var (client, dispatcher) = NewClient();
        using var list = client.Query(new GetOrders(1), QueryKey.Of("orders", "list"));
        using var detail = client.Query(new GetProfile("ada"), QueryKey.Of("orders", "detail", 5));
        await SettleAsync(list);
        await SettleAsync(detail);

        var before = dispatcher.QueryCount;
        client.Invalidate(QueryKey.Of("orders"));
        await SettleAsync(list);
        await SettleAsync(detail);

        Assert.Equal(before + 2, dispatcher.QueryCount);
    }

    [Fact]
    public async Task Exact_invalidation_touches_one_entry()
    {
        var (client, dispatcher) = NewClient();
        using var list = client.Query(new GetOrders(1), QueryKey.Of("orders", "list"));
        using var detail = client.Query(new GetProfile("ada"), QueryKey.Of("orders", "detail", 5));
        await SettleAsync(list);
        await SettleAsync(detail);

        var before = dispatcher.QueryCount;
        client.Invalidate(QueryKey.Of("orders", "list"), exact: true);
        await SettleAsync(list);
        await SettleAsync(detail);

        Assert.Equal(before + 1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_hand_written_key_and_a_derived_one_do_not_collide()
    {
        // A derived key starts with a Type and a hand-written one with a string, so they can never match
        // each other — which is what lets both live in one cache.
        var (client, dispatcher) = NewClient();
        using var derived = client.Query(new GetOrders(1));
        using var written = client.Query(new GetOrders(1), QueryKey.Of("GetOrders"));
        await SettleAsync(derived);
        await SettleAsync(written);

        var before = dispatcher.QueryCount;
        client.Invalidate(QueryKey.Of("GetOrders"));
        await SettleAsync(derived);
        await SettleAsync(written);

        Assert.Equal(before + 1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_command_can_declare_a_prefix_instead_of_a_type()
    {
        var (client, dispatcher) = NewClient();
        using var list = client.Query(new GetOrders(1), QueryKey.Of("orders", "list"));
        await SettleAsync(list);

        var before = dispatcher.QueryCount;
        await client.MutateAsync(new ArchiveEverything(1));
        await SettleAsync(list);

        Assert.Equal(before + 1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_command_can_declare_both_and_both_are_honoured()
    {
        // [Invalidates] allows multiples, and reading only the first attribute would silently honour one
        // of the two declarations — which looks exactly like a missing invalidation.
        var (client, dispatcher) = NewClient();
        using var byType = client.Query(new GetProfile("ada"));
        using var byPrefix = client.Query(new GetOrders(1), QueryKey.Of("orders", "list"));
        await SettleAsync(byType);
        await SettleAsync(byPrefix);

        var before = dispatcher.QueryCount;
        await client.MutateAsync(new SweepingChange(1));
        await SettleAsync(byType);
        await SettleAsync(byPrefix);

        Assert.Equal(before + 2, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_predicate_covers_what_a_prefix_cannot_say()
    {
        var (client, dispatcher) = NewClient();
        using var shallow = client.Query(new GetOrders(1), QueryKey.Of("orders"));
        using var deep = client.Query(new GetProfile("ada"), QueryKey.Of("orders", "detail", 5));
        await SettleAsync(shallow);
        await SettleAsync(deep);

        var before = dispatcher.QueryCount;
        client.Invalidate(key => key.Count > 2);
        await SettleAsync(shallow);
        await SettleAsync(deep);

        Assert.Equal(before + 1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_query_can_invalidate_its_own_entry()
    {
        var (client, dispatcher) = NewClient();
        using var query = client.Query(new GetOrders(1));
        using var other = client.Query(new GetOrders(2));
        await SettleAsync(query);
        await SettleAsync(other);

        var before = dispatcher.QueryCount;
        client.Invalidate(query.Key, exact: true);
        await SettleAsync(query);
        await SettleAsync(other);

        Assert.Equal(before + 1, dispatcher.QueryCount);
    }
}
