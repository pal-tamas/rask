using Rask.Cqrs;

namespace Rask.Query.Tests;

/// <summary>A command that returns a value, for the two-parameter mutation shape.</summary>
[Invalidates(typeof(GetOrders))]
public sealed record CountOrders : ICommand<int>;

public class MutationTests
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
    public async Task A_mutation_starts_idle_and_ends_successful()
    {
        var (client, _, _) = NewClient();
        var ship = client.Mutation<ShipOrder>();

        Assert.Equal(MutationStatus.Idle, ship.Status);
        Assert.False(ship.IsPending);

        await ship.RunAsync(new ShipOrder(7));

        Assert.Equal(MutationStatus.Success, ship.Status);
        Assert.True(ship.IsSuccess);
        Assert.Null(ship.Error);
    }

    [Fact]
    public async Task A_failed_mutation_records_the_error_and_does_not_throw()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.ThrowOnCommand = new InvalidOperationException("refused");
        var ship = client.Mutation<ShipOrder>();

        // It is called from an event handler, where an exception has nowhere to go and would surface
        // as an unhandled framework error rather than as something the screen can show.
        await ship.RunAsync(new ShipOrder(7));

        Assert.Equal(MutationStatus.Error, ship.Status);
        Assert.True(ship.IsError);
        Assert.Equal("refused", ship.Error?.Message);
    }

    [Fact]
    public async Task Reset_returns_a_failed_mutation_to_idle()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.ThrowOnCommand = new InvalidOperationException("refused");
        var ship = client.Mutation<ShipOrder>();
        await ship.RunAsync(new ShipOrder(7));

        ship.Reset();

        Assert.Equal(MutationStatus.Idle, ship.Status);
        Assert.Null(ship.Error);
    }

    [Fact]
    public async Task A_mutation_invalidates_what_its_command_declares()
    {
        var (client, dispatcher, _) = NewClient();
        var keep = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var orders = client.Query(new GetOrders(1), keep);
        await Settle(orders);

        dispatcher.Result = "after ship";
        await client.Mutation<ShipOrder>().RunAsync(new ShipOrder(7));
        await Settle(orders);

        Assert.Equal(2, dispatcher.QueryCountFor<GetOrders>());
        Assert.Equal("after ship", orders.Data);
    }

    [Fact]
    public async Task A_value_returning_mutation_exposes_its_result()
    {
        var (client, dispatcher, _) = NewClient();
        dispatcher.CommandResult = 7;
        var count = client.Mutation<CountOrders, int>();

        var returned = await count.RunAsync(new CountOrders());

        Assert.Equal(7, returned);
        Assert.Equal(7, count.Data);
        Assert.Equal(MutationStatus.Success, count.Status);
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
        var ship = client.Mutation<ShipOrder>()
            .Optimistic(new GetOrders(1), current => current + " (shipping)");

        var running = ship.RunAsync(new ShipOrder(7));

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
        var ship = client.Mutation<ShipOrder>()
            .Optimistic(new GetOrders(1), current => current + " (shipping)");

        await ship.RunAsync(new ShipOrder(7));

        // The whole point. A screen still showing the optimistic result after a refused save tells
        // the user something happened that did not, which is worse than never having shown it.
        Assert.Equal("first", orders.Data);
        Assert.Equal(MutationStatus.Error, ship.Status);
    }

    [Fact]
    public async Task A_successful_optimistic_edit_is_replaced_by_what_the_server_holds()
    {
        var (client, dispatcher, _) = NewClient();
        var keep = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var orders = client.Query(new GetOrders(1), keep);
        await Settle(orders);

        dispatcher.Result = "shipped";
        var ship = client.Mutation<ShipOrder>()
            .Optimistic(new GetOrders(1), current => current + " (shipping)");

        await ship.RunAsync(new ShipOrder(7));
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
        var ship = client.Mutation<ShipOrder>()
            .Optimistic(new GetOrders(9), current => current + " (shipping)");
        await ship.RunAsync(new ShipOrder(7));

        var keep = new QueryOptions { StaleTime = TimeSpan.FromHours(1) };
        using var orders = client.Query(new GetOrders(9), keep);
        await Settle(orders);

        // It must fetch rather than serve whatever the failed mutation might have left behind.
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
        var ship = client.Mutation<ShipOrder>()
            .Optimistic(new GetOrders(1), c => c + " (a)")
            .Optimistic(new GetOrders(2), c => c + " (b)");

        await ship.RunAsync(new ShipOrder(7));

        // A rollback covering only the edits made before the failure leaves the rest applied.
        Assert.Equal("first", one.Data);
        Assert.Equal("first", two.Data);
    }
}
