using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Cqrs;
using Rask.Testing;

namespace Rask.Query.Tests;

/// <summary>
///     The static <see cref="QueryClient" />: a query declared in <c>Render</c> from current values, in a
///     field or constructor from a lambda, and reached from a handler — all with nothing injected.
/// </summary>
public class StaticQueryClientTests
{
    private static readonly QueryOptions Keep = new() { StaleTime = TimeSpan.FromHours(1) };

    private static (ServiceProvider Services, CountingDispatcher Dispatcher) Session()
    {
        var dispatcher = new CountingDispatcher();
        var services = new ServiceCollection()
            .AddSingleton<IDispatcher>(dispatcher)
            .AddRaskQuery()
            .BuildServiceProvider();
        return (services, dispatcher);
    }

    private static async Task Settle<T>(Query<T> query)
    {
        if (query.Entry.InFlight is { } running)
        {
            await running;
        }
    }

    /// <summary>Declares its query in Render from the current page, like a routed page would.</summary>
    private sealed class OrdersPage : Component
    {
        public int Page { get; set; } = 1;

        public Query<string>? Seen { get; private set; }

        protected override Component? Render()
        {
            Seen = QueryClient.Query(new GetOrders(Page), Keep);
            _ = Seen.Data;
            return null;
        }
    }

    [Fact]
    public async Task A_query_in_Render_is_the_same_handle_every_render_and_follows_its_input()
    {
        var (services, dispatcher) = Session();
        var page = new OrdersPage();
        var rendered = RaskTest.Render(page, services);
        var first = page.Seen!;
        await Settle(first);

        rendered.Render();
        Assert.Same(first, page.Seen);
        Assert.Equal(1, dispatcher.QueryCount);

        dispatcher.Result = "page two";
        page.Page = 2;
        rendered.Render();
        await Settle(first);

        Assert.Same(first, page.Seen);
        Assert.Equal(new GetOrders(2), first.Key.Parts[1]);
        Assert.Equal("page two", first.Data);
    }

    /// <summary>One call site run once per row: each row is its own slot, stable across renders.</summary>
    private sealed class RowsPage : Component
    {
        public List<int> Rows { get; } = [1, 2, 3];

        public List<Query<string>> Seen { get; } = [];

        public List<Command<ShipOrder>> Ships { get; } = [];

        protected override Component? Render()
        {
            Seen.Clear();
            Ships.Clear();
            foreach (var row in Rows)
            {
                Seen.Add(QueryClient.Query(new GetOrders(row), Keep));
                Ships.Add(QueryClient.Command<ShipOrder>(key: row));
            }

            return null;
        }
    }

    [Fact]
    public void A_call_site_in_a_loop_is_one_slot_per_iteration_and_a_key_follows_the_row()
    {
        var (services, _) = Session();
        var page = new RowsPage();
        var rendered = RaskTest.Render(page, services);
        var queries = page.Seen.ToArray();
        var ships = page.Ships.ToArray();

        Assert.Equal(3, queries.Distinct().Count());
        Assert.Equal(3, ships.Distinct().Count());

        // Row 1 removed: the keyed command follows its row rather than its position.
        page.Rows.RemoveAt(0);
        rendered.Render();

        Assert.Same(ships[1], page.Ships[0]);
        Assert.Same(ships[2], page.Ships[1]);
    }

    /// <summary>Asks for a query only while <see cref="Show" /> is on.</summary>
    private sealed class TogglePage : Component
    {
        public bool Show { get; set; } = true;

        public Query<string>? Seen { get; private set; }

        protected override Component? Render()
        {
            if (Show)
            {
                Seen = QueryClient.Query(new GetOrders(1), Keep);
            }

            return null;
        }
    }

    [Fact]
    public async Task A_query_a_render_stops_using_is_set_aside_and_wakes_on_its_next_read()
    {
        var (services, dispatcher) = Session();
        var page = new TogglePage();
        var rendered = RaskTest.Render(page, services);
        var shown = page.Seen!;
        await Settle(shown);

        // Neither asked for nor read in this render: it stops watching its entry, so the cache can
        // collect it — even though this render asked for nothing at all.
        page.Show = false;
        rendered.Render();
        Assert.True(shown.IsSuspended);

        // Set aside, never killed: whoever still holds it reads it and gets its data back.
        Assert.Equal("first", shown.Data);
        Assert.False(shown.IsSuspended);
        Assert.Equal(1, dispatcher.QueryCount);
    }

    /// <summary>A child that makes its query in the constructor — which runs during its PARENT's render.</summary>
    private sealed class OrdersCard : Component
    {
        public OrdersCard() => Orders = QueryClient.Query(new GetOrders(1), Keep);

        public Query<string> Orders { get; }

        protected override Component? Render()
        {
            _ = Orders.Data;
            return null;
        }
    }

    [Fact]
    public async Task A_query_a_child_made_in_its_constructor_survives_its_parents_later_renders()
    {
        var (services, dispatcher) = Session();
        OrdersCard? card = null;

        // The constructor's call lands in the parent's render slots, and the parent's next render does not
        // repeat it (the child is reused, not rebuilt). The child's query must not die for that.
        var rendered = RaskTest.Render(() => card ??= new OrdersCard(), services);
        await Settle(card!.Orders);
        rendered.Render();
        rendered.Render();

        Assert.False(card.Orders.IsSuspended);
        dispatcher.Result = "after save";
        QueryClientFor(services).Invalidate<GetOrders>();
        await Settle(card.Orders);
        Assert.Equal("after save", card.Orders.Data);
    }

    private static IQueryClient QueryClientFor(IServiceProvider services) => services.GetRequiredService<IQueryClient>();

    /// <summary>Holds its command in a field ??= property and its query in the constructor.</summary>
    private sealed class FieldPage : Component
    {
        public FieldPage() => Orders = QueryClient.Query(() => new GetOrders(Page));

        public int Page { get; set; } = 1;

        public Query<string> Orders { get; }

        public Command<ShipOrder> Ship => field ??= QueryClient.Command<ShipOrder>();

        protected override Component? Render()
        {
            _ = Orders.Data;
            _ = Ship.IsPending;
            return null;
        }
    }

    [Fact]
    public async Task A_lambda_query_made_in_a_constructor_waits_for_its_first_read_and_follows_props()
    {
        var (services, dispatcher) = Session();

        // Constructed inside the render, the way a routed page is, so the session is reachable.
        FieldPage? page = null;
        var rendered = RaskTest.Render(() => page ??= new FieldPage(), services);
        await Settle(page!.Orders);
        Assert.Equal(new GetOrders(1), page.Orders.Key.Parts[1]);

        page.Page = 2;
        rendered.Render();
        await Settle(page.Orders);

        Assert.Equal(new GetOrders(2), page.Orders.Key.Parts[1]);
        Assert.Equal(2, dispatcher.QueryCount);
    }

    [Fact]
    public void A_lambda_does_not_run_until_the_query_is_read()
    {
        var (services, _) = Session();
        var client = services.GetRequiredService<IQueryClient>();
        var runs = 0;

        using var query = client.Query(() =>
        {
            runs++;
            return new GetOrders(1);
        });

        // A constructor runs before route parameters and props are bound; reading them here would build
        // the key from defaults.
        Assert.Equal(0, runs);
        _ = query.Data;
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task A_null_message_pauses_the_query_until_there_is_one()
    {
        var (services, dispatcher) = Session();
        var client = services.GetRequiredService<IQueryClient>();
        int? selected = null;

        using var query = client.Query(() => selected is { } id ? new GetOrders(id) : null);

        Assert.False(query.IsLoading);
        Assert.Equal(FetchStatus.Paused, query.FetchStatus);
        Assert.Equal(0, dispatcher.QueryCount);

        selected = 4;
        _ = query.Data;
        await Settle(query);

        Assert.Equal("first", query.Data);
        Assert.Equal(1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_function_query_fetches_with_the_input_its_key_was_built_from()
    {
        var (services, _) = Session();
        var client = services.GetRequiredService<IQueryClient>();
        var id = 1;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var query = client.Query(QueryKey.For<GetOrders>(), () => id, async (input, ct) =>
        {
            await gate.Task;
            return $"order {input}";
        });
        _ = query.Data;
        var firstKey = query.Key;

        // The input moves while page one's fetch is still running. That fetch must still cache page
        // one's data under page one's key — it was handed its input, rather than reading the field late.
        id = 2;
        _ = query.Data;
        gate.SetResult();
        await Settle(query);

        Assert.Equal("order 2", query.Data);
        Assert.Equal(QueryKey.For<GetOrders>(2), query.Key);
        using var back = client.Query<string>(firstKey, _ => Task.FromResult("refetched"), Keep);
        Assert.Equal("order 1", back.Data);
    }

    [Fact]
    public void An_input_lambda_binds_to_the_lambda_form_even_when_the_fetch_ignores_its_input()
    {
        var (services, _) = Session();
        var client = services.GetRequiredService<IQueryClient>();
        var id = 7;

        // Overload resolution: () => id must be the INPUT lambda, never the input itself.
        using var query = client.Query(QueryKey.For<GetOrders>(), () => id, (_, _) => Task.FromResult("x"));

        Assert.Equal(QueryKey.For<GetOrders>(7), query.Key);
    }

    [Fact]
    public async Task A_handler_reaches_its_own_sessions_cache_and_no_other()
    {
        var (alice, aliceDispatcher) = Session();
        var (bob, bobDispatcher) = Session();
        using var aliceOrders = alice.GetRequiredService<IQueryClient>().Query(new GetOrders(1), Keep);
        using var bobOrders = bob.GetRequiredService<IQueryClient>().Query(new GetOrders(1), Keep);
        await Settle(aliceOrders);
        await Settle(bobOrders);

        // What the host does around every event handler.
        using (DispatchServicesScope.Push(alice))
        {
            QueryClient.Invalidate<GetOrders>();
        }

        await Settle(aliceOrders);
        await Settle(bobOrders);

        Assert.Equal(2, aliceDispatcher.QueryCount);
        Assert.Equal(1, bobDispatcher.QueryCount);
    }

    [Fact]
    public void Outside_a_session_it_says_where_to_call_it_from()
    {
        var error = Assert.Throws<InvalidOperationException>(() => QueryClient.InvalidateAll());

        Assert.Contains("outside a session", error.Message);
        Assert.Contains("inject IQueryClient", error.Message);
    }

    [Fact]
    public void A_session_without_Rask_Query_registered_is_told_how_to_register_it()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var scope = DispatchServicesScope.Push(services);

        var error = Assert.Throws<InvalidOperationException>(() => QueryClient.InvalidateAll());

        Assert.Contains("AddRaskQuery()", error.Message);
    }
}
