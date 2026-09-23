using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Cqrs;
using Rask.Testing;

namespace Rask.Query.Tests;

public sealed record OrderPlaced(int Number) : INotification;

public sealed record WatchOrder(int Number) : ISubscription<OrderPlaced>
{
    public bool Matches(OrderPlaced placed) => placed.Number == Number;
}

public sealed record PriceTicked(decimal Price) : INotification;

/// <summary>
///     <see cref="Subscription{T}" />: declared where a query is, read the same way, fed by every publish — and closed
///     with the component that read it.
/// </summary>
public sealed class SubscriptionTests
{
    private static readonly QueryOptions Keep = new() { StaleTime = TimeSpan.FromHours(1) };

    [Fact]
    public async Task A_subscription_in_Render_holds_each_published_value_as_it_arrives()
    {
        var (services, dispatcher) = Session();
        var page = new OrdersPage();
        RaskTest.Render(page, services);

        await dispatcher.PublishAsync(new OrderPlaced(1));
        await dispatcher.PublishAsync(new OrderPlaced(2));

        await Eventually(() => page.Orders!.Data == new OrderPlaced(2));
        Assert.True(page.Orders!.IsLive);
        Assert.False(page.Orders.IsLoading);
    }

    [Fact]
    public async Task Keep_holds_the_last_values_oldest_first_and_no_more()
    {
        var (services, dispatcher) = Session();
        var page = new OrdersPage { Keep = 2 };
        RaskTest.Render(page, services);

        foreach (var number in new[] { 1, 2, 3 })
        {
            await dispatcher.PublishAsync(new OrderPlaced(number));
        }

        await Eventually(() => page.Orders!.Items.Count == 2 && page.Orders.Items[^1].Number == 3);
        Assert.Equal([new OrderPlaced(2), new OrderPlaced(3)], page.Orders!.Items);
    }

    [Fact]
    public async Task The_same_call_in_Render_is_the_same_subscription_every_render()
    {
        var (services, dispatcher) = Session();
        var page = new OrdersPage();
        var rendered = RaskTest.Render(page, services);
        var first = page.Orders;

        rendered.Render();
        rendered.Render();

        Assert.Same(first, page.Orders);
        await Eventually(() => dispatcher.SubscriberCount == 1);
    }

    [Fact]
    public async Task Into_patches_the_query_on_screen_with_no_round_trip()
    {
        var (services, dispatcher) = Session();
        var page = new PatchingPage();
        RaskTest.Render(page, services);
        await Eventually(() => page.Orders!.Data == "first");

        await dispatcher.PublishAsync(new OrderPlaced(7));

        await Eventually(() => page.Orders!.Data == "first+7");
        Assert.Equal(1, dispatcher.QueryCount);
    }

    [Fact]
    public async Task A_lambda_subscription_waits_for_its_record_then_follows_it()
    {
        var (services, dispatcher) = Session();
        FollowingCard? card = null;
        var rendered = RaskTest.Render(() => card ??= new FollowingCard(), services);

        Assert.False(card!.Orders.IsLoading);
        Assert.Equal(0, dispatcher.SubscriberCount);

        card.Selected = 1;
        rendered.Render();
        await Eventually(() => dispatcher.SubscriberCount == 1);

        card.Selected = 2;
        rendered.Render();
        await Eventually(() => dispatcher.SubscriberCount == 1 && card.Orders.IsLive);
    }

    [Fact]
    public async Task A_function_stream_that_runs_out_ends()
    {
        var (services, _) = Session();
        var page = new StreamPage(Finite);
        RaskTest.Render(page, services);

        await Eventually(() => page.Prices!.IsEnded);
        Assert.Equal(new PriceTicked(2), page.Prices!.Data);
    }

    [Fact]
    public async Task A_dropped_stream_reconnects_and_refetches_what_it_patches()
    {
        var (services, dispatcher) = Session();
        var drops = new Flaky();
        var page = new StreamPage(drops.OpenAsync) { Patch = true };
        RaskTest.Render(page, services);
        await Eventually(() => drops.Opened == 1 && page.Orders!.Data == "first");

        drops.Drop();

        await Eventually(() => page.Prices!.IsReconnecting);
        Assert.IsType<IOException>(page.Prices!.Error);
        await Eventually(() => drops.Opened == 2 && page.Prices.IsLive);
        await Eventually(() => dispatcher.QueryCount == 2);
    }

    [Fact]
    public async Task A_value_from_the_run_a_repoint_replaced_never_lands_in_the_new_one()
    {
        var (services, _) = Session();
        var streams = new Controllable();
        var page = new StreamPage(streams.OpenAsync) { Input = 1 };
        var rendered = RaskTest.Render(page, services);
        await Eventually(() => streams.Opened == 1);

        // The next order's page, with the previous order's value already handed over by the run it replaced.
        page.Input = 2;
        rendered.Render();
        await Eventually(() => streams.Opened == 2);
        streams.Yield(1, new PriceTicked(111));
        streams.Yield(2, new PriceTicked(222));

        await Eventually(() => page.Prices!.Data == new PriceTicked(222));
        await Task.Delay(100);
        Assert.Equal(new PriceTicked(222), page.Prices!.Data);
    }

    [Fact]
    public async Task A_disposed_transport_is_a_dropped_connection_not_a_refusal()
    {
        var (services, _) = Session();
        var attempts = new Attempts();
        var page = new StreamPage((_, ct) => Throwing(new ObjectDisposedException("HttpClient"), attempts, ct));
        RaskTest.Render(page, services);

        // Opened, dropped by the disposal, and opened again — rather than parked on Error, which is what a refusal gets.
        await Eventually(() => page.Prices!.IsReconnecting && page.Prices.Error is ObjectDisposedException);
        await Eventually(() => attempts.Count >= 2 && page.Prices!.IsLive);

        Assert.Null(page.Prices!.Error);
    }

    [Fact]
    public async Task A_refusal_is_final_and_is_not_retried()
    {
        var (services, _) = Session();
        var opened = 0;
        var page = new StreamPage((_, ct) =>
        {
            opened++;
            return Refused(ct);
        });
        RaskTest.Render(page, services);

        await Eventually(() => page.Prices!.IsError);
        await Task.Delay(700);

        Assert.IsType<UnauthorizedAccessException>(page.Prices!.Error);
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task A_subscription_closes_with_the_component_that_read_it()
    {
        var (services, dispatcher) = Session();
        var show = true;
        OrdersPage? page = null;
        var rendered = RaskTest.Render(() => show ? page ??= new OrdersPage() : null, services);
        await Eventually(() => dispatcher.SubscriberCount == 1);

        show = false;
        rendered.Render();

        await Eventually(() => dispatcher.SubscriberCount == 0);
    }

    [Fact]
    public async Task With_the_real_dispatcher_the_replay_is_there_before_the_first_paint()
    {
        var services = new ServiceCollection().AddRaskCqrs().AddRaskQuery().BuildServiceProvider();
        var dispatcher = services.GetRequiredService<IDispatcher>();
        RaskTest.Render(new OrdersPage(), services);
        await dispatcher.PublishAsync(new OrderPlaced(9));

        var late = new OrdersPage();
        RaskTest.Render(late, services);

        Assert.Equal(new OrderPlaced(9), late.Orders!.Data);
        Assert.True(late.Orders.IsLive);
    }

    private static (ServiceProvider Services, CountingDispatcher Dispatcher) Session()
    {
        var dispatcher = new CountingDispatcher();
        var services = new ServiceCollection()
            .AddSingleton<IDispatcher>(dispatcher)
            .AddRaskQuery()
            .BuildServiceProvider();
        return (services, dispatcher);
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

    private static async IAsyncEnumerable<PriceTicked> Finite(int symbol, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new PriceTicked(1);
        await Task.Yield();
        yield return new PriceTicked(2);
    }

    /// <summary>How many times a stream has been opened.</summary>
    private sealed class Attempts
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public int Next() => Interlocked.Increment(ref _count);
    }

    // Throws once, then runs: what a transport disposed under a restarting host looks like.
    private static async IAsyncEnumerable<PriceTicked> Throwing(
        Exception first,
        Attempts attempts,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var attempt = attempts.Next();
        await Task.Yield();
        if (attempt == 1)
        {
            throw first;
        }

        yield return new PriceTicked(attempt);
        await Task.Delay(Timeout.Infinite, ct);
    }

    private static async IAsyncEnumerable<PriceTicked> Refused(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        throw new UnauthorizedAccessException("not yours");
#pragma warning disable CS0162 // An iterator needs a yield to be one.
        yield break;
#pragma warning restore CS0162
    }

    /// <summary>Subscribes in Render, like a routed page would.</summary>
    private sealed class OrdersPage : Component
    {
        public int? Keep { get; init; }

        public Subscription<OrderPlaced>? Orders { get; private set; }

        protected override Component? Render()
        {
            Orders = QueryClient.Subscribe<OrderPlaced>();
            if (Keep is { } keep)
            {
                Orders.Keep(keep);
            }

            _ = Orders.Data;
            return null;
        }
    }

    /// <summary>Patches the query it shows with every order.</summary>
    private sealed class PatchingPage : Component
    {
        public Query<string>? Orders { get; private set; }

        protected override Component? Render()
        {
            Orders = QueryClient.Query(new GetOrders(1), Keep);
            var placed = QueryClient.Subscribe<OrderPlaced>().Into(Orders, (list, order) => $"{list}+{order.Number}");
            _ = Orders.Data;
            _ = placed.Data;
            return null;
        }
    }

    /// <summary>Holds a lambda subscription in a field, waiting for a selection.</summary>
    private sealed class FollowingCard : Component
    {
        public int? Selected { get; set; }

        public Subscription<OrderPlaced> Orders =>
            field ??= QueryClient.Subscribe<OrderPlaced>(
                () => Selected is { } number ? new WatchOrder(number) : null);

        protected override Component? Render()
        {
            _ = Orders.Data;
            return null;
        }
    }

    /// <summary>A function stream in Render, optionally patching a query.</summary>
    private sealed class StreamPage(Func<int, CancellationToken, IAsyncEnumerable<PriceTicked>> open) : Component
    {
        public bool Patch { get; init; }

        public int Input { get; set; } = 1;

        public Subscription<PriceTicked>? Prices { get; private set; }

        public Query<string>? Orders { get; private set; }

        protected override Component? Render()
        {
            Prices = QueryClient.Subscribe(Input, open);
            if (Patch)
            {
                Orders = QueryClient.Query(new GetOrders(1));
                Prices.Into(Orders, (text, _) => text);
                _ = Orders.Data;
            }

            _ = Prices.Data;
            return null;
        }
    }

    /// <summary>One stream per input, each yielding only what the test hands it.</summary>
    private sealed class Controllable
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, System.Threading.Channels.Channel<PriceTicked>> _byInput = new();
        private int _opened;

        public int Opened => Volatile.Read(ref _opened);

        public void Yield(int input, PriceTicked value) => Channel(input).Writer.TryWrite(value);

        public async IAsyncEnumerable<PriceTicked> OpenAsync(int input, [EnumeratorCancellation] CancellationToken ct)
        {
            Interlocked.Increment(ref _opened);
            await foreach (var value in Channel(input).Reader.ReadAllAsync(ct))
            {
                yield return value;
            }
        }

        private System.Threading.Channels.Channel<PriceTicked> Channel(int input) =>
            _byInput.GetOrAdd(input, static _ => System.Threading.Channels.Channel.CreateUnbounded<PriceTicked>());
    }

    /// <summary>A stream the test can cut, as a dropped connection would.</summary>
    private sealed class Flaky
    {
        private TaskCompletionSource _drop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _opened;

        public int Opened => Volatile.Read(ref _opened);

        public void Drop()
        {
            var drop = _drop;
            _drop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            drop.TrySetException(new IOException("connection reset"));
        }

        public async IAsyncEnumerable<PriceTicked> OpenAsync(int symbol, [EnumeratorCancellation] CancellationToken ct)
        {
            Interlocked.Increment(ref _opened);
            yield return new PriceTicked(symbol);
            await _drop.Task.WaitAsync(ct);
        }
    }
}
