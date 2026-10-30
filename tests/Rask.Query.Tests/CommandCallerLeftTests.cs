using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core.Live;

namespace Rask.Querying.Tests;

/// <summary>
///     A command once sent runs to its end; a caller that has gone meanwhile — the page left, the component
///     unmounted — gets no answer: no error, the status it had, as a browser drops the answer to a request its page
///     has left. A timeout, the host stopping or a real failure is still an error.
/// </summary>
public class CommandCallerLeftTests
{
    private static readonly QueryOptions Keep = new() { StaleTime = TimeSpan.FromHours(1) };

    private static (SessionQueryClient Client, CountingDispatcher Dispatcher) NewClient()
    {
        var dispatcher = new CountingDispatcher();
        return (new SessionQueryClient(dispatcher, new TestClock(DateTimeOffset.UnixEpoch)), dispatcher);
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
    public async Task A_command_whose_caller_left_runs_to_its_end_and_its_answer_is_dropped_silently()
    {
        var (client, dispatcher) = NewClient();
        using var orders = client.Query(new GetOrders(1), Keep);
        await Settle(orders);
        dispatcher.CommandGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ship = client.Command<ShipOrder>();
        using var page = new CancellationTokenSource();
        var running = ship.Send(new ShipOrder(7), page.Token).AsTask();

        await page.CancelAsync();
        dispatcher.Result = "after ship";
        dispatcher.CommandGate.SetResult();
        await running;
        await Settle(orders);

        Assert.Equal(1, dispatcher.CommandsCompleted);
        Assert.Equal(CommandStatus.Idle, ship.Status);
        Assert.Null(ship.Error);
        Assert.Equal("after ship", orders.Data);
    }

    [Fact]
    public async Task A_command_whose_caller_left_keeps_its_result_and_the_status_it_had()
    {
        var (client, dispatcher) = NewClient();
        dispatcher.CommandResult = 7;
        var count = client.Command<CountOrders, int>();
        await count.Send(new CountOrders(), TestContext.Current.CancellationToken);
        dispatcher.CommandResult = 8;
        dispatcher.CommandGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var page = new CancellationTokenSource();
        var running = count.Send(new CountOrders(), page.Token).AsTask();

        await page.CancelAsync();
        dispatcher.CommandGate.SetResult();
        var returned = await running;

        Assert.Equal(8, returned);
        Assert.Equal(8, count.Data);
        Assert.Equal(CommandStatus.Success, count.Status);
    }

    [Fact]
    public async Task A_command_that_fails_after_its_caller_left_is_rolled_back_with_no_error()
    {
        var (client, dispatcher) = NewClient();
        using var orders = client.Query(new GetOrders(1), Keep);
        await Settle(orders);
        dispatcher.CommandGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.ThrowOnCommand = new InvalidOperationException("refused");
        var ship = client.Command<ShipOrder>();
        using var page = new CancellationTokenSource();
        var running = ship.Send(new ShipOrder(7), page.Token)
            .Optimistically(orders.Optimistic(current => current + " (shipping)"))
            .AsTask();

        await page.CancelAsync();
        dispatcher.CommandGate.SetResult();
        await running;

        Assert.Equal(CommandStatus.Idle, ship.Status);
        Assert.Null(ship.Error);
        Assert.Equal("first", orders.Data);
    }

    [Fact]
    public async Task A_command_whose_outcome_is_unknown_after_its_caller_left_asks_the_server_again()
    {
        var (client, dispatcher) = NewClient();
        using var orders = client.Query(new GetOrders(1), Keep);
        await Settle(orders);
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = client.Command(invalidates: typeof(GetOrders));
        using var page = new CancellationTokenSource();
        var running = save.Send(async _ =>
            {
                await aborted.Task;
                throw new TaskCanceledException("the browser aborted the fetch");
            }, page.Token)
            .Optimistically(orders.Optimistic(current => current + " (saving)"))
            .AsTask();

        await page.CancelAsync();
        dispatcher.Result = "what the server holds";
        aborted.SetResult();
        await running;
        await Settle(orders);

        Assert.Equal(CommandStatus.Idle, save.Status);
        Assert.Null(save.Error);
        Assert.Equal(2, dispatcher.QueryCountFor<GetOrders>());
        Assert.Equal("what the server holds", orders.Data);
    }

    [Fact]
    public async Task A_command_whose_caller_left_before_it_was_sent_is_never_sent()
    {
        var (client, dispatcher) = NewClient();
        var ship = client.Command<ShipOrder>();
        using var page = new CancellationTokenSource();
        await page.CancelAsync();

        await ship.Send(new ShipOrder(7), page.Token);

        Assert.Equal(0, dispatcher.CommandCount);
        Assert.Equal(CommandStatus.Idle, ship.Status);
    }

    [Fact]
    public async Task A_command_that_times_out_is_still_an_error_and_is_rolled_back()
    {
        var (client, dispatcher) = NewClient();
        using var orders = client.Query(new GetOrders(1), Keep);
        await Settle(orders);
        dispatcher.CommandGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ship = client.Command<ShipOrder>();
        using var timeout = new CancellationTokenSource();
        using var lifetime = new CancellationTokenSource();
        using var handler = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, timeout.Token);
        Task running;
        using (DispatchEventTokenScope.Push(null, timeout.Token, handler.Token))
        {
            running = ship.Send(new ShipOrder(7), handler.Token)
                .Optimistically(orders.Optimistic(current => current + " (shipping)"))
                .AsTask();
        }

        await timeout.CancelAsync();
        await running;

        Assert.Equal(CommandStatus.Error, ship.Status);
        Assert.IsAssignableFrom<OperationCanceledException>(ship.Error);
        Assert.Equal("first", orders.Data);
        Assert.Equal(0, dispatcher.CommandsCompleted);
    }

    [Fact]
    public async Task A_command_the_host_stops_is_still_an_error()
    {
        var (client, dispatcher) = NewClient();
        dispatcher.CommandGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new StoppingHost();
        await using var services = new ServiceCollection()
            .AddSingleton<IHostApplicationLifetime>(host)
            .BuildServiceProvider();
        var ship = client.Command<ShipOrder>();
        Task running;
        using (Ambient.Enter(services))
        {
            running = ship.Send(new ShipOrder(7), TestContext.Current.CancellationToken).AsTask();
        }

        host.Stop();
        await running;

        Assert.Equal(CommandStatus.Error, ship.Status);
        Assert.IsAssignableFrom<OperationCanceledException>(ship.Error);
        Assert.Equal(0, dispatcher.CommandsCompleted);
    }

    [Fact]
    public async Task A_command_that_throws_with_its_caller_still_there_is_an_error_and_is_rolled_back()
    {
        var (client, dispatcher) = NewClient();
        using var orders = client.Query(new GetOrders(1), Keep);
        await Settle(orders);
        dispatcher.ThrowOnCommand = new InvalidOperationException("refused");
        var ship = client.Command<ShipOrder>();
        using var page = new CancellationTokenSource();

        await ship.Send(new ShipOrder(7), page.Token)
            .Optimistically(orders.Optimistic(current => current + " (shipping)"));

        Assert.Equal(CommandStatus.Error, ship.Status);
        Assert.Equal("refused", ship.Error?.Message);
        Assert.Equal("first", orders.Data);
    }

    private sealed class StoppingHost : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopped.Cancel();

        public void Stop() => StopApplication();

        public void Dispose() => _stopped.Dispose();
    }
}
