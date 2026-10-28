using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Cqrs;
using Rask.Testing;

namespace Rask.Querying.Tests;

/// <summary>
///     A change that lands while its reader is rendering — from the render's own thread, or from a stream's thread
///     in the middle of the read — still reaches that reader.
/// </summary>
public sealed class ChangeDuringReadTests
{
    private static readonly QueryOptions Fresh = new() { StaleTime = TimeSpan.FromHours(1) };

    [Fact]
    public void A_value_arriving_after_the_reader_read_in_the_same_render_is_shown()
    {
        var (services, _) = Session();
        var feed = new InlineFeed();

        var rendered = Page.Render(new FeedPage(feed, f => f.Push(new PriceTicked(2))), services);

        Assert.Contains("price:2 status:Live", rendered.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stream_ending_after_the_reader_read_in_the_same_render_shows_the_end()
    {
        var (services, _) = Session();
        var feed = new InlineFeed();

        var rendered = Page.Render(new FeedPage(feed, f => f.End()), services);

        Assert.Contains("status:Ended", rendered.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_arriving_after_the_reader_read_in_the_same_render_shows_the_error()
    {
        var (services, _) = Session();
        var feed = new InlineFeed();

        var rendered = Page.Render(new FeedPage(feed, f => f.Fail(new UnauthorizedAccessException("not yours"))), services);

        Assert.Contains("status:Error", rendered.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_set_after_the_reader_read_in_the_same_render_is_shown()
    {
        var (services, _) = Session();
        var rendered = Page.Render(new SettingPage(), services);
        rendered.Shows("orders:first");

        await rendered.Click("Patch");

        Assert.Contains("orders:patched", rendered.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reader_is_in_the_list_before_anyone_can_see_it_observed()
    {
        var probe = new ObservingProbe(rounds: 2_000);

        Page.Render(probe);

        Assert.Equal(0, probe.SeenObservedWithNobodyReading);
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

    /// <summary>
    ///     A stream whose values, end and error run straight through to the subscription on the caller's thread, so a
    ///     render that pushes one lands it in the middle of that render.
    /// </summary>
    private sealed class InlineFeed
    {
        private readonly Channel<PriceTicked> _channel = Channel.CreateUnbounded<PriceTicked>(
            new UnboundedChannelOptions { AllowSynchronousContinuations = true, SingleReader = true });

        public void Push(PriceTicked value) => _channel.Writer.TryWrite(value);

        public void End() => _channel.Writer.TryComplete();

        public void Fail(Exception error) => _channel.Writer.TryComplete(error);

        public async IAsyncEnumerable<PriceTicked> OpenAsync(int symbol, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var value in _channel.Reader.ReadAllAsync(ct))
            {
                yield return value;
            }
        }
    }

    /// <summary>Reads its subscription, then — once — makes the stream move before the render returns.</summary>
    private sealed class FeedPage(InlineFeed feed, Action<InlineFeed> afterReading) : Component
    {
        private bool _moved;

        protected override Component? Render()
        {
            var prices = QueryClient.Subscribe(1, feed.OpenAsync);
            var shown = $"price:{prices.Data?.Price} status:{prices.Status}";
            if (!_moved)
            {
                _moved = true;
                afterReading(feed);
            }

            return P[shown];
        }
    }

    /// <summary>Reads its query, then — when asked — sets what it holds before the render returns.</summary>
    private sealed class SettingPage : Component
    {
        private bool _patch;

        protected override Component? Render()
        {
            var orders = QueryClient.Query(new GetOrders(1), Fresh);
            var shown = "orders:" + orders.Data;
            if (_patch)
            {
                _patch = false;
                QueryClient.Set(new GetOrders(1), "patched");
            }

            return Div[P[shown], Button.OnClick(() => { _patch = true; })["Patch"]];
        }
    }

    /// <summary>
    ///     Registers itself as a reader, round after round, while another thread asks the readers what a stream's value
    ///     asks them: observed, and is anybody still reading?
    /// </summary>
    private sealed class ObservingProbe(int rounds) : Component
    {
        private ComponentReaders? _round;
        private int _answered;

        public int SeenObservedWithNobodyReading { get; private set; }

        protected override Component? Render()
        {
            var asker = new Thread(Ask) { IsBackground = true };
            asker.Start();
            for (var i = 0; i < rounds; i++)
            {
                var readers = new ComponentReaders();
                Volatile.Write(ref _round, readers);
                readers.Observe();
                while (Volatile.Read(ref _answered) <= i && asker.IsAlive)
                {
                    Thread.Yield();
                }
            }

            Volatile.Write(ref _round, null);
            asker.Join();
            return null;
        }

        private void Ask()
        {
            ComponentReaders? last = null;
            for (var i = 0; i < rounds; i++)
            {
                ComponentReaders? readers;
                while ((readers = Volatile.Read(ref _round)) is null || ReferenceEquals(readers, last))
                {
                    Thread.Yield();
                }

                while (!readers.EverObserved)
                {
                    // Spinning, not yielding: the window this looks for is a handful of instructions. The barrier
                    // keeps the read inside the loop.
                    Interlocked.MemoryBarrier();
                }

                if (!AnyoneReading(readers))
                {
                    SeenObservedWithNobodyReading++;
                }

                last = readers;
                Interlocked.Increment(ref _answered);
            }
        }

        private static bool AnyoneReading(ComponentReaders readers)
        {
            try
            {
                return readers.HasLiveReaders;
            }
            catch (Exception)
            {
                // A list torn by the render adding to it while this walks it — out of range, a null slot, "collection
                // was modified": just as wrong an answer, and it must fail this test rather than take the runner down.
                return false;
            }
        }
    }
}
