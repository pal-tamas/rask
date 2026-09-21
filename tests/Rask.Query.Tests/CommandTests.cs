using Rask.Cqrs;

namespace Rask.Query.Tests;

/// <summary>A command that returns a value, for the two-parameter command shape.</summary>
[Invalidates(typeof(GetOrders))]
public sealed record CountOrders : ICommand<int>;

public class CommandTests
{
    private static (SessionQueryClient Client, CountingDispatcher Dispatcher, TestClock Time) NewClient()
    {
        var dispatcher = new CountingDispatcher();
        var time = new TestClock(DateTimeOffset.UnixEpoch);
        return (new SessionQueryClient(dispatcher, time), dispatcher, time);
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
    public async Task A_command_starts_idle_and_ends_successful()
    {
        var (client, _, _) = NewClient();
        var ship = client.Command<ShipOrder>();

        Assert.Equal(CommandStatus.Idle, ship.Status);
        Assert.False(ship.IsPending);

        await ship.SendAsync(new ShipOrder(7));

        Assert.Equal(CommandStatus.Success, ship.Status);
        Assert.True(ship.IsSuccess);
        Assert.Null(ship.Error);
    }

    [Fact]
    public async Task A_failed_command_records_the_error_and_does_not_throw()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.ThrowOnCommand = new InvalidOperationException("refused");
        var ship = client.Command<ShipOrder>();

        // It is called from an event handler, where an exception has nowhere to go and would surface
        // as an unhandled framework error rather than as something the screen can show.
        await ship.SendAsync(new ShipOrder(7));

        Assert.Equal(CommandStatus.Error, ship.Status);
        Assert.True(ship.IsError);
        Assert.Equal("refused", ship.Error?.Message);
    }

    [Fact]
    public async Task Reset_returns_a_failed_command_to_idle()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.ThrowOnCommand = new InvalidOperationException("refused");
        var ship = client.Command<ShipOrder>();
        await ship.SendAsync(new ShipOrder(7));

        ship.Reset();

        Assert.Equal(CommandStatus.Idle, ship.Status);
        Assert.Null(ship.Error);
    }

    [Fact]
    public async Task A_command_invalidates_what_its_command_declares()
    {
        var (client, dispatcher, _) = NewClient();
        var keep = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var orders = client.Query(new GetOrders(1), keep);
        await Settle(orders);

        dispatcher.Result = "after ship";
        await client.Command<ShipOrder>().SendAsync(new ShipOrder(7));
        await Settle(orders);

        Assert.Equal(2, dispatcher.QueryCountFor<GetOrders>());
        Assert.Equal("after ship", orders.Data);
    }

    [Fact]
    public async Task A_value_returning_command_exposes_its_result()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.CommandResult = 7;
        var count = client.Command<CountOrders, int>();

        var returned = await count.SendAsync(new CountOrders());

        Assert.Equal(7, returned);
        Assert.Equal(7, count.Data);
        Assert.Equal(CommandStatus.Success, count.Status);
    }

    // ---------------------------------------------------------------- optimistic

    [Fact]
    public async Task An_optimistic_edit_is_visible_before_the_server_answers()
    {
        var (client, dispatcher, _) = NewClient();
        var keep = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var orders = client.Query(new GetOrders(1), keep);
        await Settle(orders);
        Assert.Equal("first", orders.Data);

        dispatcher.Block();
        var ship = client.Command<ShipOrder>()
            .Optimistic(new GetOrders(1), current => current + " (shipping)");

        var running = ship.SendAsync(new ShipOrder(7));

        Assert.Equal("first (shipping)", orders.Data);

        dispatcher.Release();
        await running;
    }

    [Fact]
    public async Task A_failed_optimistic_edit_is_rolled_back()
    {
        var (client, dispatcher, _) = NewClient();
        var keep = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var orders = client.Query(new GetOrders(1), keep);
        await Settle(orders);

        dispatcher.ThrowOnCommand = new InvalidOperationException("refused");
        var ship = client.Command<ShipOrder>()
            .Optimistic(new GetOrders(1), current => current + " (shipping)");

        await ship.SendAsync(new ShipOrder(7));

        // The whole point. A screen still showing the optimistic result after a refused save tells
        // the user something happened that did not, which is worse than never having shown it.
        Assert.Equal("first", orders.Data);
        Assert.Equal(CommandStatus.Error, ship.Status);
    }

    [Fact]
    public async Task A_successful_optimistic_edit_is_replaced_by_what_the_server_holds()
    {
        var (client, dispatcher, _) = NewClient();
        var keep = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var orders = client.Query(new GetOrders(1), keep);
        await Settle(orders);

        dispatcher.Result = "shipped";
        var ship = client.Command<ShipOrder>()
            .Optimistic(new GetOrders(1), current => current + " (shipping)");

        await ship.SendAsync(new ShipOrder(7));
        await Settle(orders);

        // The guess is not kept: the command's [Invalidates] refetches and the truth wins.
        Assert.Equal("shipped", orders.Data);
    }

    [Fact]
    public async Task Rolling_back_an_entry_that_held_nothing_makes_it_fetch()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.ThrowOnCommand = new InvalidOperationException("refused");

        // Nothing is cached for this query, so there is nothing to edit and nothing to put back.
        var ship = client.Command<ShipOrder>()
            .Optimistic(new GetOrders(9), current => current + " (shipping)");
        await ship.SendAsync(new ShipOrder(7));

        var keep = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var orders = client.Query(new GetOrders(9), keep);
        await Settle(orders);

        // It must fetch rather than serve whatever the failed command might have left behind.
        Assert.Equal("first", orders.Data);
        Assert.Equal(1, dispatcher.QueryCountFor<GetOrders>());
    }

    [Fact]
    public async Task Every_optimistic_edit_is_rolled_back_not_just_the_first()
    {
        var (client, dispatcher, _) = NewClient();
        var keep = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var one = client.Query(new GetOrders(1), keep);
        using var two = client.Query(new GetOrders(2), keep);
        await Settle(one);
        await Settle(two);

        dispatcher.ThrowOnCommand = new InvalidOperationException("refused");
        var ship = client.Command<ShipOrder>()
            .Optimistic(new GetOrders(1), c => c + " (a)")
            .Optimistic(new GetOrders(2), c => c + " (b)");

        await ship.SendAsync(new ShipOrder(7));

        // A rollback covering only the edits made before the failure leaves the rest applied.
        Assert.Equal("first", one.Data);
        Assert.Equal("first", two.Data);
    }

    /// <summary>A type with data but no message — the shape of a Rask.Data aggregate.</summary>
    private sealed record Person;

    private static readonly QueryOptions Keep = new() { StaleTime = TimeSpan.FromHours(1) };

    [Fact]
    public async Task A_function_command_runs_its_lambda_and_invalidates_the_keys_it_was_created_with()
    {
        var (client, _, _) = NewClient();
        var loads = 0;
        using var people = client.Query("people", _ => Task.FromResult(++loads), Keep);
        await Settle(people);
        var save = client.Command(invalidates: "people");
        var ran = false;

        await save.SendAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        await Settle(people);

        Assert.True(ran);
        Assert.Equal(CommandStatus.Success, save.Status);
        Assert.Equal(2, people.Data);
    }

    [Fact]
    public async Task A_failed_function_command_records_the_error_and_invalidates_nothing()
    {
        var (client, _, _) = NewClient();
        var loads = 0;
        using var people = client.Query("people", _ => Task.FromResult(++loads), Keep);
        await Settle(people);
        var save = client.Command(invalidates: "people");

        await save.SendAsync(_ => Task.FromException(new InvalidOperationException("refused")));
        await Settle(people);

        Assert.True(save.IsError);
        Assert.Equal("refused", save.Error?.Message);
        Assert.Equal(1, people.Data);
    }

    [Fact]
    public async Task A_value_returning_function_command_returns_its_result_or_default_on_failure()
    {
        var (client, _, _) = NewClient();
        var save = client.Command();

        var created = await save.SendAsync(_ => Task.FromResult("person 7"));
        var refused = await save.SendAsync(_ => Task.FromException<string>(new InvalidOperationException()));

        Assert.Equal("person 7", created);
        Assert.Null(refused);
        Assert.True(save.IsError);
    }

    [Fact]
    public async Task A_type_key_is_reached_by_every_way_of_naming_the_type()
    {
        var (client, _, _) = NewClient();
        var active = 0;
        var other = 0;
        using var people = client.Query(QueryKey.For<Person>("active"), _ => Task.FromResult(++active), Keep);
        using var orders = client.Query("orders", _ => Task.FromResult(++other), Keep);
        await Settle(people);
        await Settle(orders);

        // The type is the first part, so a command naming the type reaches every key about it by prefix
        // — and nothing else.
        await client.Command(invalidates: typeof(Person)).SendAsync(_ => Task.CompletedTask);
        await Settle(people);
        await Settle(orders);
        client.Invalidate<Person>();
        await Settle(people);

        Assert.Equal(3, people.Data);
        Assert.Equal(1, orders.Data);
    }

    [Fact]
    public void A_type_key_is_the_type_followed_by_its_parts()
    {
        Assert.Equal(QueryKey.Of(typeof(Person), "active", 2), QueryKey.For<Person>("active", 2));
        Assert.Equal(QueryKey.Of(typeof(Person)), (QueryKey)typeof(Person));
        Assert.True(QueryKey.For<Person>("active").Matches(typeof(Person)));
    }

    [Fact]
    public async Task Several_keys_are_several_prefixes_whether_types_or_strings()
    {
        var (client, _, _) = NewClient();
        var people = 0;
        var dashboard = 0;
        using var a = client.Query(QueryKey.For<Person>(), _ => Task.FromResult(++people), Keep);
        using var b = client.Query("dashboard", _ => Task.FromResult(++dashboard), Keep);
        await Settle(a);
        await Settle(b);

        await client.Command(invalidates: [typeof(Person), "dashboard"]).SendAsync(_ => Task.CompletedTask);
        await Settle(a);
        await Settle(b);

        Assert.Equal(2, a.Data);
        Assert.Equal(2, b.Data);
    }
}
