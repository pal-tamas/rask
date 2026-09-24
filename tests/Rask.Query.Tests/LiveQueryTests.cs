using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Query.Tests;

/// <summary>A stand-in for a Rask.Data aggregate: only its name travels in a <see cref="DataChanged" />.</summary>
public sealed class Order;

public sealed class Customer;

/// <summary>A message query that says what it reads, the read-side mirror of <c>[Invalidates]</c>.</summary>
[Live(typeof(Order))]
public sealed record GetLiveOrders(int Page) : IQuery<string>;

/// <summary>The same query without the declaration: keyed by itself, so no write can reach it.</summary>
public sealed record GetQuietOrders(int Page) : IQuery<string>;

/// <summary>
///     A live query refetches when anything in the process writes what it reads, not only when this session does —
///     declared once where the query is, never at the call site.
/// </summary>
public sealed class LiveQueryTests
{
    private static readonly QueryOptions Keep = new() { StaleTime = TimeSpan.FromHours(1) };

    [Fact]
    public async Task A_query_keyed_by_its_entity_is_live_with_nothing_declared()
    {
        var (client, dispatcher, clock) = Session();
        var fetches = 0;
        var query = client.Query(QueryKey.For<Order>(), Counting(() => fetches++), Keep);
        await Settled(query);
        var before = fetches;

        await Changed(dispatcher, clock, typeof(Order));

        await Eventually(() => fetches > before);
    }

    [Fact]
    public async Task A_message_query_is_live_when_it_declares_what_it_reads()
    {
        var (client, dispatcher, clock) = Session();
        var query = client.Query(new GetLiveOrders(1), Keep);
        await Settled(query);
        var before = dispatcher.QueryCountFor<GetLiveOrders>();

        await Changed(dispatcher, clock, typeof(Order));

        await Eventually(() => dispatcher.QueryCountFor<GetLiveOrders>() > before);
    }

    [Fact]
    public async Task A_message_query_that_declares_nothing_is_not_live()
    {
        // The honest half of the rule: a message query is keyed by ITSELF, so a write cannot know it reads orders.
        // It stays exactly as it was rather than guessing — the same reason a command states [Invalidates].
        var (client, dispatcher, clock) = Session();
        var query = client.Query(new GetQuietOrders(1), Keep);
        await Settled(query);
        var before = dispatcher.QueryCountFor<GetQuietOrders>();

        await Changed(dispatcher, clock, typeof(Order));

        await Task.Delay(150);
        Assert.Equal(before, dispatcher.QueryCountFor<GetQuietOrders>());
    }

    [Fact]
    public async Task A_live_query_ignores_a_write_to_another_entity()
    {
        var (client, dispatcher, clock) = Session();
        var fetches = 0;
        var query = client.Query(QueryKey.For<Order>(), Counting(() => fetches++), Keep);
        await Settled(query);
        var before = fetches;

        await Changed(dispatcher, clock, typeof(Customer));

        await Task.Delay(150);
        Assert.Equal(before, fetches);
    }

    [Fact]
    public async Task A_live_query_ignores_the_change_it_is_replayed_when_it_opens()
    {
        var (client, dispatcher, clock) = Session();
        var fetches = 0;
        var query = client.Query(QueryKey.For<Order>(), Counting(() => fetches++), Keep);
        await Settled(query);
        var before = fetches;

        // A subscription starts with the last value published. That write happened before this listener existed,
        // so the fetch that brought the page up already reflects it: acting on it would cost a second request.
        await dispatcher.Publish(
            new DataChanged(typeof(Order).FullName!, clock.GetUtcNow() - TimeSpan.FromMinutes(1)));

        await Task.Delay(150);
        Assert.Equal(before, fetches);
    }

    [Fact]
    public async Task A_key_that_names_no_type_is_not_live()
    {
        var (client, dispatcher, clock) = Session();
        var fetches = 0;
        var query = client.Query("just-a-name", Counting(() => fetches++), Keep);
        await Settled(query);
        var before = fetches;

        await Changed(dispatcher, clock, typeof(Order));

        await Task.Delay(150);
        Assert.Equal(before, fetches);
    }

    [Fact]
    public async Task One_listener_serves_every_live_query_in_a_session()
    {
        var (client, dispatcher, _) = Session();

        client.Query(QueryKey.For<Order>("a"), Counting(() => { }), Keep);
        client.Query(QueryKey.For<Order>("b"), Counting(() => { }), Keep);
        client.Query(new GetLiveOrders(1), Keep);

        await Eventually(() => dispatcher.SubscriberCount == 1);
        await Task.Delay(100);
        Assert.Equal(1, dispatcher.SubscriberCount);
    }

    [Fact]
    public async Task Two_live_queries_about_one_entity_both_refetch()
    {
        var (client, dispatcher, clock) = Session();
        var a = 0;
        var b = 0;
        var orders = client.Query(QueryKey.For<Order>("a"), Counting(() => a++), Keep);
        var archive = client.Query(QueryKey.For<Order>("b"), Counting(() => b++), Keep);
        await Settled(orders);
        await Settled(archive);
        var beforeA = a;
        var beforeB = b;

        await Changed(dispatcher, clock, typeof(Order));

        await Eventually(() => a > beforeA && b > beforeB);
    }

    private static Func<CancellationToken, Task<string>> Counting(Action onFetch) =>
        _ =>
        {
            onFetch();
            return Task.FromResult("value");
        };

    private static (IQueryClient Client, CountingDispatcher Dispatcher, TestClock Clock) Session()
    {
        var dispatcher = new CountingDispatcher();
        var clock = new TestClock(DateTimeOffset.UnixEpoch.AddYears(55));
        var services = new ServiceCollection()
            .AddSingleton<IDispatcher>(dispatcher)
            .AddSingleton<TimeProvider>(clock)
            .AddRaskQuery()
            .BuildServiceProvider();
        return (services.GetRequiredService<IQueryClient>(), dispatcher, clock);
    }

    // Published after the listener opened, which is what separates a real change from the replay above.
    private static Task Changed(CountingDispatcher dispatcher, TestClock clock, Type entity) =>
        dispatcher.Publish(new DataChanged(entity.FullName!, clock.GetUtcNow() + TimeSpan.FromSeconds(1)));

    private static async Task Settled<T>(Query<T> query)
    {
        await Eventually(() => query.Data is not null);

        // The listener opens on a background task; a publish before it has registered would reach nobody and the
        // test would fail for a reason that has nothing to do with being live.
        await Task.Delay(100);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the condition never held");
            await Task.Delay(10);
        }
    }
}
