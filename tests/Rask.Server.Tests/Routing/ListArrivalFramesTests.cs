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
// navigation sends the page it goes to at once, with its address, and what its OnMount and OnUpdated read follows
// in frames of their own. The reads are held and released by the test, so nothing here depends on how fast the
// machine is.
[Collection("HostEnvironment")]
public sealed class ListArrivalFramesTests(ITestOutputHelper output)
{
    private const string LastRow = "Viszonylat 15";
    private const string Heading = "Viszonylatok</";

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
    public async Task A_list_reached_by_a_link_is_sent_at_once_with_its_address_and_its_rows_follow()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("link", "/destinations");
        var atOnce = await arrival.Next();
        store.Release();
        var after = await arrival.Until(LastRow);

        // Nothing is cancelled: the reads are still running, and what they bring arrives as its own frame.
        Assert.Contains(Heading, atOnce, StringComparison.Ordinal);
        Assert.DoesNotContain(LastRow, atOnce, StringComparison.Ordinal);
        Assert.Equal("/destinations", LiveFrames.HistoryUrl(atOnce));
        Assert.Null(LiveFrames.HistoryUrl(Assert.Single(after)));
    }

    [Fact]
    public async Task A_save_that_goes_to_the_list_sends_the_list_with_its_address_and_the_rows_follow()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("save", "/destinations");
        var atOnce = await arrival.Next();
        store.Release();
        var after = await arrival.Until(LastRow);

        // The handler's own repaint is withheld once it has navigated, so the list never travels without its address.
        Assert.Contains(Heading, atOnce, StringComparison.Ordinal);
        Assert.Equal("/destinations", LiveFrames.HistoryUrl(atOnce));
        Assert.Null(LiveFrames.HistoryUrl(after[^1]));
    }

    [Fact]
    public async Task A_table_that_arrives_after_its_page_at_the_end_of_its_level_follows_it_as_a_diff()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("link", "/destinations-tail");
        var atOnce = await arrival.Next();
        store.Release();
        var after = await arrival.Until(LastRow);

        Assert.Equal("/destinations-tail", LiveFrames.HistoryUrl(atOnce));
        Assert.Equal("diff", Kind(Assert.Single(after)));
    }

    [Fact]
    public async Task A_list_that_says_loading_where_its_table_will_be_shows_it_until_its_rows_replace_it()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("link", "/destinations-loading");
        var atOnce = await arrival.Next();
        store.Release();
        var after = await arrival.Until(LastRow);

        Assert.Contains("id=\\\"loading\\\"", atOnce, StringComparison.Ordinal);
        Assert.DoesNotContain(LastRow, atOnce, StringComparison.Ordinal);
        Assert.Contains(LastRow, Assert.Single(after), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_edit_page_reached_by_a_link_is_sent_at_once_with_its_address_and_its_record_follows_with_its_title()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("link", "/destinations/3");
        var atOnce = await arrival.Next();
        store.Release();
        var after = await arrival.Until("Viszonylat 3");

        Assert.Equal("/destinations/3", LiveFrames.HistoryUrl(atOnce));
        Assert.DoesNotContain("Viszonylat 3", atOnce, StringComparison.Ordinal);
        Assert.Contains("Viszonylat 3 m&#xF3;dos&#xED;t&#xE1;sa | PPTA", Assert.Single(after), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_sends_the_reader_on_after_an_await_ends_on_its_destination_with_its_address_and_rows()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);

        await arrival.Go("link", "/moved");
        var frames = await arrival.Until(LastRow);

        Assert.Contains(frames, static f => LiveFrames.HistoryUrl(f) == "/destinations");
    }

    [Fact]
    public async Task A_page_whose_read_never_comes_back_is_painted_at_once_and_the_session_keeps_answering()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("link", "/destinations");
        var atOnce = await arrival.Next();
        await arrival.Go("link", "/destinations/new");
        var next = Assert.Single(await arrival.Settled());

        Assert.Equal("/destinations", LiveFrames.HistoryUrl(atOnce));
        Assert.DoesNotContain(LastRow, atOnce, StringComparison.Ordinal);
        Assert.Equal("/destinations/new", LiveFrames.HistoryUrl(next));
    }

    [Fact]
    public async Task A_page_with_nothing_to_wait_for_is_one_frame_with_its_address()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations", output);

        await arrival.Go("link", "/destinations/new");
        var frame = Assert.Single(await arrival.Settled());

        Assert.Equal("/destinations/new", LiveFrames.HistoryUrl(frame));
    }

    // How much of the layout travels with it is the differ's business, and is not held here.
    [Fact]
    public async Task A_navigation_between_two_pages_of_one_layout_is_one_frame_with_the_new_page_and_its_address()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations", output);

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
