using Rask.Core.Diagnostics;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// StateHasChanged from a thread that is not rendering: a component follows a source outside the page — a
// singleton's event raised by a background poller — and is asked to render from whatever thread raised it, while
// another thread may be rendering it. The request is never lost, the component's own bookkeeping is never torn,
// and a session asked without end says who asked.
[Collection("ConsoleRedirect")]
public sealed partial class FollowedSourceDirtyMarkTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_request_from_another_thread_inside_a_render_that_then_throws_is_drawn_by_the_next_render()
    {
        var sp = RenderHarness.EmptyServices();
        var child = new Follower();
        var host = new FollowerHost(child);
        host.RenderAsLiveRoot(sp);
        child.DuringRender(() =>
        {
            child.FollowFromOutside("new");
            throw new InvalidOperationException("the render fails after the source moved");
        });

        host.PropsChanged = true;
        Assert.Throws<InvalidOperationException>(() => host.RenderAsLiveRoot(sp));
        child.DuringRender(null);
        host.PropsChanged = false;
        var html = host.RenderAsLiveRoot(sp);

        Assert.Contains("new", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requests_from_many_threads_while_another_renders_lose_nothing_and_tear_nothing()
    {
        var sp = RenderHarness.EmptyServices();
        var first = new Follower();
        var second = new Follower();
        var host = new FollowerHost(first, second);
        host.RenderAsLiveRoot(sp);
        using var stop = new CancellationTokenSource();
        var value = 0;
        var asking = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(
            () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var next = Interlocked.Increment(ref value).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    first.FollowOnThisThread(next);
                    second.FollowOnThisThread(next);
                }
            },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        for (var render = 0; render < 2_000; render++)
        {
            host.RenderAsLiveRoot(sp);
        }

        await stop.CancelAsync();
        await Task.WhenAll(asking);
        first.FollowOnThisThread("last");
        second.FollowOnThisThread("last");
        var html = host.RenderAsLiveRoot(sp);

        Assert.Equal(2, html.Split("last").Length - 1);
        Assert.Equal(1, first.FirstRenders);
        Assert.Equal(1, second.FirstRenders);
        Assert.False(first.IsRenderRequestedForTest);
        Assert.False(second.IsRenderRequestedForTest);
    }

    [Fact]
    public void Renders_asked_for_without_end_are_made_at_once_up_to_the_streak_and_then_after_a_pause_one_at_a_time()
    {
        var pace = new RenderPace();

        var atFullSpeed = Enumerable.Range(1, RenderPace.Streak - 1).Select(_ => pace.Next(null, chained: true)).ToArray();
        var atTheStreak = pace.Next(null, chained: true);
        var whileOneWaits = pace.Next(null, chained: true);
        pace.PauseOver();
        var afterItsPause = pace.Next(null, chained: true);

        Assert.All(atFullSpeed, static turn => Assert.Equal(RenderPace.Turn.Now, turn));
        Assert.Equal(RenderPace.Turn.AfterAPause, atTheStreak);
        Assert.Equal(RenderPace.Turn.Taken, whileOneWaits);
        Assert.Equal(RenderPace.Turn.AfterAPause, afterItsPause);
    }

    [Fact]
    public void Renders_left_behind_by_events_are_made_at_once_however_many_and_start_no_row()
    {
        var pace = new RenderPace();

        var afterEvents = Enumerable.Range(1, RenderPace.Streak * 3).Select(_ => pace.Next(null, chained: false)).ToArray();
        var thenARow = Enumerable.Range(1, RenderPace.Streak - 1).Select(_ => pace.Next(null, chained: true)).ToArray();

        Assert.All(afterEvents, static turn => Assert.Equal(RenderPace.Turn.Now, turn));
        Assert.All(thenARow, static turn => Assert.Equal(RenderPace.Turn.Now, turn));
    }

    [Fact]
    public void A_session_slowed_for_being_asked_without_end_says_once_which_components_asked()
    {
        var pace = new RenderPace();
        var reported = Reported(() =>
        {
            for (var render = 1; render <= RenderPace.Streak + 5; render++)
            {
                pace.Asked(new Follower());
                pace.Asked(new FollowerHost());
                pace.Next("s1", chained: true);
            }
        });

        var said = Assert.Single(reported, static e => e.Message.Contains("renders in a row", StringComparison.Ordinal));

        Assert.Equal(RaskLogLevel.Warning, said.Level);
        Assert.Equal("Rask.Live", said.Category);
        Assert.Contains("Session s1", said.Message, StringComparison.Ordinal);
        Assert.Contains("Asked by: Follower, FollowerHost.", said.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Renders_that_each_take_longer_than_the_quiet_are_still_a_row_and_are_slowed()
    {
        // A loaded machine: every render takes two seconds, and the next starts the moment the last one ends.
        var clock = new ManualClock();
        var pace = new RenderPace(clock);

        var turns = Enumerable.Range(1, RenderPace.Streak).Select(_ =>
        {
            pace.Began();
            clock.Advance(TimeSpan.FromSeconds(2));
            return pace.Next(null, chained: true);
        }).ToArray();

        Assert.All(turns[..^1], static turn => Assert.Equal(RenderPace.Turn.Now, turn));
        Assert.Equal(RenderPace.Turn.AfterAPause, turns[^1]);
    }

    [Fact]
    public void A_quiet_between_one_render_and_the_next_ends_the_row()
    {
        var clock = new ManualClock();
        var pace = new RenderPace(clock);
        for (var render = 1; render < RenderPace.Streak; render++)
        {
            pace.Began();
            pace.Next(null, chained: true);
        }

        clock.Advance(RenderPace.Quiet + TimeSpan.FromMilliseconds(1));
        pace.Began();
        var afterTheQuiet = pace.Next(null, chained: true);

        Assert.Equal(RenderPace.Turn.Now, afterTheQuiet);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _now = 1;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref _now);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _now, by.Ticks);
    }

    private static List<RaskDiagnosticEvent> Reported(Action act)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = e =>
        {
            lock (captured)
            {
                captured.Add(e);
            }
        };
        try
        {
            act();
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        return captured;
    }

    private sealed class FollowerHost(params Follower[] children) : Component
    {
        /// <summary>Whether the next render hands its children changed props.</summary>
        public bool PropsChanged { get; set; }

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var drawn = new Component[children.Length];
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                drawn[i] = ctx.GetOrCreate(_ => child);
                LiveRenderContext.NotifyParameters(drawn[i], PropsChanged);
            }

            return Div[drawn];
        }
    }

    // Draws what a source outside the page last told it.
    private sealed class Follower : Component
    {
        private volatile string _shown = "old";
        private int _firstRenders;

        private Action? _duringRender;

        /// <summary>Runs <paramref name="act" /> inside the next renders, after the state they draw has been read.</summary>
        public void DuringRender(Action? act) => _duringRender = act;

        public int FirstRenders => Volatile.Read(ref _firstRenders);

        public void FollowOnThisThread(string shown)
        {
            _shown = shown;
            StateHasChanged();
        }

        public void FollowFromOutside(string shown)
        {
            var outside = new Thread(() => FollowOnThisThread(shown));
            outside.Start();
            outside.Join();
        }

        protected override Task OnFirstRender()
        {
            Interlocked.Increment(ref _firstRenders);
            return Task.CompletedTask;
        }

        protected override Component? Render()
        {
            var shown = _shown;
            _duringRender?.Invoke();
            return Span[shown];
        }
    }
}
