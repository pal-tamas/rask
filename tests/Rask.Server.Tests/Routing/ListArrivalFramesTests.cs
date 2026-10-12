using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

// What a page reached IN PLACE costs, frame by frame, on the app the report came from (ListArrivalApp): a
// navigation waits for the page it goes to — its OnMount and OnUpdated — within the navigation budget, and the
// page is sent once. The reads are held and released by the test, and the budget is either longer than a test
// may run or over before the read is let go, so nothing here depends on how fast the machine is.
[Collection("HostEnvironment")]
public sealed class ListArrivalFramesTests(ITestOutputHelper output)
{
    private const string LastRow = "Viszonylat 15";
    private const string Heading = "Viszonylatok</";

    // Longer than any wait here may last: a page is sent because its reads came back, never because time ran out.
    private static void Patient(RaskServerOptions o) => o.NavigationQuiescenceTimeout = TimeSpan.FromMinutes(5);

    // Over while the test still holds the read.
    private static void Brief(RaskServerOptions o) => o.NavigationQuiescenceTimeout = TimeSpan.FromMilliseconds(40);

    [Fact]
    public async Task A_first_request_for_the_list_waits_for_both_reads_and_is_one_document_with_its_rows()
    {
        using var host = Host(new ListArrivalStore());

        var html = await host.Http.GetStringAsync("/destinations", TestContext.Current.CancellationToken);

        output.WriteLine($"first GET: {Encoding.UTF8.GetByteCount(html)} B");
        Assert.Contains(LastRow, html, StringComparison.Ordinal);
        Assert.Contains("Viszonylatok | PPTA</title>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_request_whose_reads_outlast_the_budget_is_the_page_without_rows_and_the_socket_brings_them()
    {
        var store = new ListArrivalStore();
        store.Hold();
        await using var arrival = await Arrival.Open(
            store, "/destinations", output, configure: o => o.QuiescenceTimeout = TimeSpan.FromMilliseconds(50));

        var first = arrival.Html;
        store.Release();
        var frames = await arrival.Until(LastRow);

        Assert.DoesNotContain(LastRow, first, StringComparison.Ordinal);
        Assert.Contains(LastRow, Assert.Single(frames), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_list_reached_by_a_link_arrives_once_with_its_rows_and_its_address()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output, Patient);
        store.Hold();

        await arrival.Go("link", "/destinations");
        await store.ReadStarted();
        store.Release();
        var frame = Assert.Single(await arrival.Settled());

        Assert.Contains(Heading, frame, StringComparison.Ordinal);
        Assert.Contains(LastRow, frame, StringComparison.Ordinal);
        Assert.Equal("/destinations", LiveFrames.HistoryUrl(frame));
    }

    [Fact]
    public async Task A_save_that_goes_to_the_list_is_one_frame_with_the_list_and_its_address()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output, Patient);
        store.Hold();

        await arrival.Go("save", "/destinations");
        await store.ReadStarted();
        store.Release();
        var frame = Assert.Single(await arrival.Settled());

        // The handler's own repaint is withheld once it has navigated, so the list never travels without its address.
        Assert.Contains(LastRow, frame, StringComparison.Ordinal);
        Assert.Equal("/destinations", LiveFrames.HistoryUrl(frame));
    }

    [Fact]
    public async Task A_list_whose_reads_outlast_the_budget_is_sent_without_rows_at_the_budget_and_the_rows_follow()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output, Brief);
        store.Hold();

        await arrival.Go("link", "/destinations");
        var atTheBudget = await arrival.Next();
        store.Release();
        var after = await arrival.Until(LastRow);

        // Spending the budget cancels nothing: the reads are still running, and what follows arrives as before.
        Assert.Contains(Heading, atTheBudget, StringComparison.Ordinal);
        Assert.DoesNotContain(LastRow, atTheBudget, StringComparison.Ordinal);
        Assert.Equal("/destinations", LiveFrames.HistoryUrl(atTheBudget));
        Assert.Null(LiveFrames.HistoryUrl(Assert.Single(after)));
    }

    [Fact]
    public async Task A_table_that_arrives_after_the_budget_at_the_end_of_its_level_follows_its_page_as_a_diff()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output, Brief);
        store.Hold();

        await arrival.Go("link", "/destinations-tail");
        var atTheBudget = await arrival.Next();
        store.Release();
        var after = await arrival.Until(LastRow);

        Assert.Equal("/destinations-tail", LiveFrames.HistoryUrl(atTheBudget));
        Assert.Equal("diff", Kind(Assert.Single(after)));
    }

    [Fact]
    public async Task A_list_that_says_loading_where_its_table_will_be_never_shows_it_when_the_read_is_within_the_budget()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output, Patient);
        store.Hold();

        await arrival.Go("link", "/destinations-loading");
        await store.ReadStarted();
        store.Release();
        var frame = Assert.Single(await arrival.Settled());

        // Neither as markup writes it nor as a diff's text would.
        Assert.DoesNotContain("Bet&#xF6;lt", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("Betölt", frame, StringComparison.Ordinal);
        Assert.Contains(LastRow, frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_edit_page_reached_by_a_link_arrives_as_its_record_with_its_title_and_never_as_no_such_record()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output, Patient);
        store.Hold();

        await arrival.Go("link", "/destinations/3");
        await store.ReadStarted();
        store.Release();
        var frame = Assert.Single(await arrival.Settled());

        Assert.DoesNotContain("Nincs ilyen viszonylat", frame, StringComparison.Ordinal);
        Assert.Contains("Viszonylat 3 m&#xF3;dos&#xED;t&#xE1;sa | PPTA</title>", frame, StringComparison.Ordinal);
        Assert.Equal("/destinations/3", LiveFrames.HistoryUrl(frame));
    }

    [Fact]
    public async Task A_page_that_sends_the_reader_on_after_an_await_is_never_sent_and_its_destination_arrives_once()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output, Patient);

        await arrival.Go("link", "/moved");
        var frame = Assert.Single(await arrival.Settled());

        Assert.DoesNotContain("id=\\\"moved\\\"", frame, StringComparison.Ordinal);
        Assert.DoesNotContain(">moved<", frame, StringComparison.Ordinal);
        Assert.Contains(LastRow, frame, StringComparison.Ordinal);
        Assert.Equal("/destinations", LiveFrames.HistoryUrl(frame));
    }

    [Fact]
    public async Task A_second_link_pressed_while_the_first_page_is_awaited_is_the_only_page_sent()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output, Patient);
        store.Hold();

        await arrival.Go("link", "/destinations");
        await store.ReadStarted();
        await arrival.Go("link", "/destinations/new");
        var frame = Assert.Single(await arrival.Settled());
        store.Release();

        // The reader did not sit out the first page's budget: its wait ended when the second link was read.
        Assert.Equal("/destinations/new", LiveFrames.HistoryUrl(frame));
        Assert.DoesNotContain(Heading, frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_whose_read_never_comes_back_is_painted_at_the_budget_and_the_session_keeps_answering()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output, Brief);
        store.Hold();

        await arrival.Go("link", "/destinations");
        var atTheBudget = await arrival.Next();
        await arrival.Go("link", "/destinations/new");
        var next = Assert.Single(await arrival.Settled());

        Assert.Equal("/destinations", LiveFrames.HistoryUrl(atTheBudget));
        Assert.DoesNotContain(LastRow, atTheBudget, StringComparison.Ordinal);
        Assert.Equal("/destinations/new", LiveFrames.HistoryUrl(next));
    }

    [Fact]
    public async Task A_page_with_nothing_to_wait_for_is_sent_without_waiting_however_long_the_budget()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations", output, Patient);

        await arrival.Go("link", "/destinations/new");
        var frame = await arrival.Next();

        // The budget here is longer than this wait may last: a navigation that sat it out would never be read.
        Assert.Equal("/destinations/new", LiveFrames.HistoryUrl(frame));
    }

    // How much of the layout travels with it is the differ's business, and is not held here.
    [Fact]
    public async Task A_navigation_between_two_pages_of_one_layout_is_one_frame_with_the_new_page_and_its_address()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations", output, Patient);

        await arrival.Go("link", "/destinations/new");
        var frame = Assert.Single(await arrival.Settled());

        Assert.Equal("/destinations/new", LiveFrames.HistoryUrl(frame));
        Assert.Contains("Ment", frame, StringComparison.Ordinal);
    }

    // What the socket's permessage-deflate makes of a frame, near enough: one message, no shared window.
    private static long Deflated(string text)
    {
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(Encoding.UTF8.GetBytes(text));
        }

        return packed.Length;
    }

    private static string Kind(string frame) =>
        frame.StartsWith("{\"html\":", StringComparison.Ordinal) ? "page"
        : frame.StartsWith("{\"kind\":\"diff\"", StringComparison.Ordinal) ? "diff"
        : "other";

    private static RaskTestHost Host(ListArrivalStore store, Action<RaskServerOptions>? configure = null) =>
        RaskTestHost.Create<ListArrivalApp>(
            configureServices: services => services.AddSingleton(store), environment: "Development", configureServer: configure);

    private sealed class Arrival(RaskTestHost host, WebSocket ws, ITestOutputHelper output, Action restoreSink) : IAsyncDisposable
    {
        private readonly ConcurrentQueue<RaskDiagnosticEvent> _said = new();

        public string Html { get; private init; } = string.Empty;

        // Why each whole page went out, as the session said it on Rask.Live.
        public List<string> Why =>
            [.. _said.Where(static e => e.Message.Contains("went out as the whole page", StringComparison.Ordinal)).Select(static e => e.Message)];

        public static async Task<Arrival> Open(
            ListArrivalStore store, string path, ITestOutputHelper output, Action<RaskServerOptions>? configure = null)
        {
            var host = Host(store, configure);
            var html = await host.Http.GetStringAsync(path);
            var sessionId = MarkupAssert.SessionId(html);
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);

            // After the host is up: mapping it points the sink at its own logger.
            var previous = RaskDiagnostics.Sink;
            var arrival = new Arrival(host, ws, output, () => RaskDiagnostics.Sink = previous) { Html = html };
            RaskDiagnostics.Sink = arrival._said.Enqueue;
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            await ws.AttachedAsync(host, sessionId);
            output.WriteLine($"GET {path}: {Encoding.UTF8.GetByteCount(html)} B ({Deflated(html)} B deflated)");
            return arrival;
        }

        // By the link the reader presses, or by the button whose handler saves and goes.
        public Task Go(string by, string path)
        {
            if (by == "link")
            {
                return ws.SendJsonAsync(new { type = "navigate", path, query = "" });
            }

            var handler = Regex.Match(
                Html, $"id=\"{Regex.Escape(by)}\"[^>]*data-rask-on-click=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
            return ws.SendJsonAsync(new { id = handler.Groups[1].Value, type = "click" });
        }

        // Every frame the session sent for what it has been sent so far, once it has finished all of it.
        public async Task<List<string>> Settled()
        {
            var frames = await ws.SettledAsync();
            frames.ForEach(Note);
            return frames;
        }

        public async Task<string> Next()
        {
            var frame = await ws.ReceiveUntilAsync(static f => !f.StartsWith("{\"type\":\"ack\"", StringComparison.Ordinal), "a frame");
            Note(frame);
            return frame;
        }

        // Every frame up to and with the one that shows the text.
        public async Task<List<string>> Until(string text)
        {
            var frames = new List<string>();
            while (frames.Count == 0 || !frames[^1].Contains(text, StringComparison.Ordinal))
            {
                frames.Add(await Next());
            }

            return frames;
        }

        private void Note(string frame)
        {
            var why = Why.Count > 0 ? Why[^1] : string.Empty;
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  frame {Kind(frame)} {Encoding.UTF8.GetByteCount(frame)} B ({Deflated(frame)} B deflated) history={LiveFrames.HistoryUrl(frame)} rows={frame.Contains(LastRow, StringComparison.Ordinal)} why={why[..Math.Min(why.Length, 700)]}"));
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
