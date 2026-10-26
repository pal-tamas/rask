using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Live;

// A page that follows a singleton's event (BoardApp): the event is raised from threads that are not the
// session's, in bursts, and may land anywhere in a dispatch — while its handler runs, inside any of its renders,
// after its read. Whatever was last written is on the reader's page without another event, a component that asks
// for a render from every render is slowed and named, and the session goes on answering.
//
// Every point is chosen by the test from inside the page's own Render, and every burst has returned before that
// Render does, so nothing here depends on how fast the machine is.
[Collection("HostEnvironment")]
public sealed class FollowedSourceRenderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task A_burst_at_every_point_of_a_dispatch_leaves_the_last_value_on_the_page_without_another_event(int lastWalk)
    {
        var board = new PumpBoard();
        await using var page = await Page.Open(board);
        board.InHandler = () => board.Burst(1);
        board.Arm(walk =>
        {
            if (walk <= lastWalk)
            {
                board.Burst(walk + 1);
            }
        });

        await page.Click("filter");
        var frame = await page.Until($"notices {lastWalk + 1}.", $"page {lastWalk + 1}.");

        Assert.Contains("row 3 from october.", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_that_comes_back_inside_the_dispatchs_last_render_is_on_the_page_without_another_event()
    {
        var board = new PumpBoard();
        await using var page = await Page.Open(board);
        board.Hold();
        board.Arm(walk =>
        {
            if (walk < 3)
            {
                board.Burst(walk);
            }
            else
            {
                board.ReleaseFromOutside();
            }
        });

        await page.Click("filter");
        var frame = await page.Until("row 3 from october.");

        Assert.Contains("notices 2.", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_burst_inside_a_render_is_one_more_render_however_many_threads_raised_it()
    {
        var board = new PumpBoard();
        await using var page = await Page.Open(board);
        board.Arm(walk =>
        {
            if (walk == 1)
            {
                board.Burst(7, threads: 16);
            }
        });

        await page.Click("filter");
        await page.Until("notices 7.");
        await page.Settled();

        Assert.Equal(2, board.Walks);
    }

    // The first raise finds the session idle and renders it on the thread that raised. That render is held inside
    // the header until every other thread has raised, so all of them land while it is being made.
    [Fact]
    public async Task A_burst_that_lands_while_an_idle_session_renders_is_one_more_render_however_many_threads_raised_it()
    {
        const int Threads = 16;
        var board = new PumpBoard();
        await using var page = await Page.Open(board);
        using var theRestHaveRaised = new ManualResetEventSlim();
        var raised = 0;
        board.Arm(static _ => { }, walk =>
        {
            if (walk == 1)
            {
                theRestHaveRaised.Wait(TestContext.Current.CancellationToken);
            }
        });

        board.Burst(9, Threads, () =>
        {
            if (Interlocked.Increment(ref raised) >= Threads - 1)
            {
                theRestHaveRaised.Set();
            }
        });
        await page.Until("notices 9.", "page 9.");
        await page.Settled();

        // The render that was held, and one for everything raised while it was.
        Assert.Equal(2, board.NoticesWalks);
        Assert.InRange(board.Walks, 1, 2);
    }

    [Fact]
    public async Task A_page_that_asks_for_a_render_from_every_render_is_slowed_and_named_and_the_session_still_answers()
    {
        var board = new PumpBoard();
        await using var page = await Page.Open(board);

        await page.Navigate("/board/runaway");
        var warning = await page.Said("BoardRunawayPage");
        var rendersWhenNamed = Volatile.Read(ref board.RunawayRenders);
        var started = TimeProvider.System.GetTimestamp();
        await Rask.TestSupport.WaitFor.True(
            () => Volatile.Read(ref board.RunawayRenders) >= rendersWhenNamed + 3, "three more renders of the runaway page");
        var threeMoreTook = TimeProvider.System.GetElapsedTime(started);
        await page.Navigate("/board/list");
        var answered = await page.Until("row 3 from ever.");

        Assert.Equal(RaskLogLevel.Warning, warning.Level);
        Assert.InRange(rendersWhenNamed, 1, 64);
        Assert.True(
            threeMoreTook >= RenderPace.Pause,
            $"three more renders of a runaway took {threeMoreTook}, which is not slowed to one every {RenderPace.Pause}.");
        Assert.Equal("/board/list", LiveFrames.HistoryUrl(answered));
    }

    [Fact]
    public async Task Bursts_from_many_threads_while_filters_dispatch_lose_nothing_and_throw_nothing()
    {
        var board = new PumpBoard();
        await using var page = await Page.Open(board);
        board.Arm(static _ => { });
        using var stop = new CancellationTokenSource();
        var value = 0;
        var raisers = Enumerable.Range(0, 3).Select(_ => Task.Factory.StartNew(
            () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    board.Burst(Interlocked.Increment(ref value), threads: 3);
                }
            },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        for (var filter = 1; filter <= 20; filter++)
        {
            await page.Navigate("/board/list", $"from=day{filter}");
            await page.Until($"row 3 from day{filter}.");
        }

        await stop.CancelAsync();
        await Task.WhenAll(raisers);
        board.Burst(int.MaxValue);
        var last = await page.Until($"notices {int.MaxValue}.", $"page {int.MaxValue}.");

        Assert.Contains("row 3 from day20.", last, StringComparison.Ordinal);
        Assert.DoesNotContain(page.Heard, static e => e.Exception is not null || e.Level >= RaskLogLevel.Error);
    }

    private sealed class Page(RaskTestHost host, WebSocket ws, string html, Action restoreSink) : IAsyncDisposable
    {
        private readonly ConcurrentQueue<RaskDiagnosticEvent> _heard = new();

        /// <summary>Everything the framework reported while this page was open.</summary>
        public RaskDiagnosticEvent[] Heard => [.. _heard];

        public static async Task<Page> Open(PumpBoard board, Action<RaskServerOptions>? configure = null)
        {
            // Whole pages, so a frame says everything the reader sees.
            var host = RaskTestHost.Create<BoardApp>(
                configureServices: services => services.AddSingleton(board),
                configureServer: configure,
                diffMode: LiveDiffMode.DisabledFull);
            var html = await host.Http.GetStringAsync("/board/list");
            var sessionId = MarkupAssert.SessionId(html);
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);

            // After the host is up: mapping it points the sink at its own logger.
            var previous = RaskDiagnostics.Sink;
            var page = new Page(host, ws, html, () => RaskDiagnostics.Sink = previous);
            RaskDiagnostics.Sink = page._heard.Enqueue;
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            await ws.AttachedAsync(host, sessionId);
            return page;
        }

        public Task Click(string id)
        {
            var handler = Regex.Match(
                html, $"id=\"{Regex.Escape(id)}\"[^>]*data-rask-on-click=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
            return ws.SendJsonAsync(new { id = handler.Groups[1].Value, type = "click" });
        }

        public Task Navigate(string path, string query = "") => ws.SendJsonAsync(new { type = "navigate", path, query });

        /// <summary>The first frame that shows every one of <paramref name="texts" />.</summary>
        public Task<string> Until(params string[] texts) =>
            ws.ReceiveUntilAsync(
                frame => texts.All(text => frame.Contains(text, StringComparison.Ordinal)),
                $"a frame that shows '{string.Join("' and '", texts)}'");

        public Task<List<string>> Settled() => ws.SettledAsync();

        /// <summary>The first report that names <paramref name="what" />.</summary>
        public async Task<RaskDiagnosticEvent> Said(string what)
        {
            await Rask.TestSupport.WaitFor.True(
                () => _heard.Any(e => e.Message.Contains(what, StringComparison.Ordinal)), $"a report that names {what}");
            return _heard.First(e => e.Message.Contains(what, StringComparison.Ordinal));
        }

        public async ValueTask DisposeAsync()
        {
            restoreSink();
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
                // The server may already have closed it.
            }

            ws.Dispose();
            host.Dispose();
        }
    }
}
