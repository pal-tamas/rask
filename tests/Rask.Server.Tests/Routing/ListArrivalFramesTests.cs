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

// What a page reached IN PLACE costs today, frame by frame, on the app the report came from (ListArrivalApp):
// a page whose OnUpdated awaits is sent before the await is back, and sent again after it. Each test PINS what
// happens now and says what it should become; the ones named "today" flip when the navigation waits.
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
        Assert.Equal("page", Kind(Assert.Single(frames)));
    }

    [Fact]
    public async Task Today_a_list_reached_by_a_link_is_sent_before_its_reads_and_again_after_as_two_whole_pages()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("link", "/destinations");
        var before = await arrival.Next();
        store.Release();
        var after = await arrival.Until(LastRow);

        // Wanted: ONE frame, with the rows. Today the heading goes first and the table follows.
        Assert.Equal("page", Kind(before));
        Assert.Contains(Heading, before, StringComparison.Ordinal);
        Assert.DoesNotContain(LastRow, before, StringComparison.Ordinal);
        Assert.Equal("/destinations", LiveFrames.HistoryUrl(before));
        Assert.Equal("page", Kind(Assert.Single(after)));
        Assert.Contains("is by position, replaced by <div class=\"block overflow-auto\"><table", arrival.Why[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Today_a_save_that_goes_to_the_list_is_three_frames_and_the_address_moves_after_the_page()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("save", "/destinations");
        var before = await arrival.Next();
        var address = await arrival.Next();
        store.Release();
        var after = await arrival.Until(LastRow);

        // Wanted: ONE frame, with the rows and the address. Today the handler's own repaint sends the list
        // before the dispatch has taken the address, which then travels alone.
        Assert.Equal("page", Kind(before));
        Assert.Null(LiveFrames.HistoryUrl(before));
        Assert.DoesNotContain(LastRow, before, StringComparison.Ordinal);
        Assert.StartsWith("{\"kind\":\"diff\",\"ops\":[],\"history\":", address, StringComparison.Ordinal);
        Assert.Equal("/destinations", LiveFrames.HistoryUrl(address));
        Assert.Equal("page", Kind(Assert.Single(after)));
    }

    [Fact]
    public async Task A_table_that_arrives_at_the_end_of_its_level_follows_its_page_as_a_diff()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("link", "/destinations-tail");
        var before = await arrival.Next();
        store.Release();
        var after = await arrival.Until(LastRow);

        Assert.Equal("page", Kind(before));
        Assert.Equal("diff", Kind(Assert.Single(after)));
    }

    [Fact]
    public async Task Today_a_list_that_says_loading_where_its_table_will_be_is_two_whole_pages_too()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("link", "/destinations-loading");
        var before = await arrival.Next();
        store.Release();
        var after = await arrival.Until(LastRow);

        Assert.Equal("page", Kind(before));
        Assert.Contains("Bet&#xF6;lt&#xE9;s", before, StringComparison.Ordinal);
        Assert.Equal("page", Kind(Assert.Single(after)));
        Assert.Contains("RemoveSubtree at /1/1/3/1 is by position, replaced by", arrival.Why[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Today_an_edit_page_reached_by_a_link_shows_no_such_record_until_its_read_is_back()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);
        store.Hold();

        await arrival.Go("link", "/destinations/3");
        var before = await arrival.Next();
        store.Release();
        var after = await arrival.Until("Viszonylat 3 m");

        // Wanted: the record's page in one frame. Today the "no such record" branch is painted first.
        Assert.Equal("page", Kind(before));
        Assert.Contains("Nincs ilyen viszonylat", before, StringComparison.Ordinal);
        Assert.Contains(">PPTA</title>", before, StringComparison.Ordinal);
        var second = Assert.Single(after);
        Assert.Equal("page", Kind(second));
        Assert.DoesNotContain("Nincs ilyen viszonylat", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Today_a_page_that_sends_the_reader_on_after_an_await_is_painted_before_it_does()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations/new", output);

        await arrival.Go("link", "/moved");
        var frames = await arrival.Until(LastRow);

        // Wanted: the list, once. Today the page that is about to leave is sent first.
        Assert.Contains(frames, static f => f.Contains("id=\\\"moved\\\"", StringComparison.Ordinal));
        Assert.Equal("/destinations", LiveFrames.HistoryUrl(frames[^1]) ?? LiveFrames.HistoryUrl(frames[^2]));
    }

    [Fact]
    public async Task A_navigation_between_two_pages_of_one_layout_is_a_whole_page_though_only_the_outlet_changed()
    {
        var store = new ListArrivalStore();
        await using var arrival = await Arrival.Open(store, "/destinations", output);

        await arrival.Go("link", "/destinations/new");
        var frame = await arrival.Next();

        // Wanted: a diff of the outlet, the crumb and the title. Today the sidebar travels again.
        Assert.Equal("page", Kind(frame));
        Assert.Contains("data-ui-sidebar-item", frame, StringComparison.Ordinal);
        Assert.Contains("is by position", arrival.Why[^1], StringComparison.Ordinal);
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
