using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Cqrs;
using Rask.Querying;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

// A page that reads through Rask.Query where it renders, reached in place: the page is sent at once with its
// address — the query's spinner while its first fetch runs — and the rows follow when the fetch is back. What is
// already in the session's cache is shown at once with no fetch, and a query a save has just made out of date is
// refetched, so the reader who saved ends on what they saved.
public sealed class QueryArrivalFramesTests(ITestOutputHelper output)
{
    private const string Fresh = "order v1-3";

    [Fact]
    public async Task A_list_that_reads_through_a_query_reached_by_a_link_shows_its_spinner_at_once_and_then_its_rows()
    {
        await using var arrival = await Arrival.Open(output);
        arrival.Store.Hold();

        await arrival.Navigate("/q/list");
        var atOnce = await arrival.Next();
        arrival.Store.Release();
        var rows = await arrival.Until(Fresh);

        Assert.Contains("spinner", atOnce, StringComparison.Ordinal);
        Assert.Equal("/q/list", LiveFrames.HistoryUrl(atOnce));
        Assert.Null(LiveFrames.HistoryUrl(rows));
    }

    [Fact]
    public async Task A_list_that_reads_through_a_query_reached_by_Go_from_a_handler_shows_its_spinner_at_once_and_then_its_rows()
    {
        await using var arrival = await Arrival.Open(output);
        arrival.Store.Hold();

        await arrival.Press("go");
        var atOnce = await arrival.Next();
        arrival.Store.Release();
        var rows = await arrival.Until(Fresh);

        Assert.Contains("spinner", atOnce, StringComparison.Ordinal);
        Assert.Equal("/q/list", LiveFrames.HistoryUrl(atOnce));
        Assert.Null(LiveFrames.HistoryUrl(rows));
    }

    [Fact]
    public async Task A_second_visit_to_a_list_whose_query_is_cached_is_one_frame_with_its_rows_and_no_fetch()
    {
        await using var arrival = await Arrival.Open(output);
        await arrival.Navigate("/q/list");
        await arrival.Until(Fresh);
        await arrival.Navigate("/q/home");
        await arrival.Settled();
        var readsBefore = arrival.Store.Reads;
        arrival.Store.Hold();

        await arrival.Navigate("/q/list");
        var frame = Assert.Single(await arrival.Settled());

        // The fetch is held, so a visit that fetched would show its spinner.
        Assert.Contains(Fresh, frame, StringComparison.Ordinal);
        Assert.Equal(readsBefore, arrival.Store.Reads);
    }

    [Fact]
    public async Task A_save_that_makes_a_cached_list_out_of_date_and_goes_to_it_ends_on_the_fresh_rows()
    {
        await using var arrival = await Arrival.Open(output);
        await arrival.Navigate("/q/list");
        await arrival.Until(Fresh);
        await arrival.Navigate("/q/home");
        await arrival.Settled();
        arrival.Store.Hold();

        await arrival.Press("save");
        var atOnce = await arrival.Next();
        arrival.Store.Release();
        var fresh = await arrival.Until("order v2-3");

        Assert.Equal("/q/list", LiveFrames.HistoryUrl(atOnce));
        Assert.DoesNotContain("order v2-", atOnce, StringComparison.Ordinal);
        Assert.DoesNotContain("order v1-", fresh, StringComparison.Ordinal);
    }

    // Every frame is a whole page here, so the page a frame shows — and the handlers on it — can be read from it.
    private sealed class Arrival(RaskTestHost host, WebSocket ws, ITestOutputHelper output, string html) : IAsyncDisposable
    {
        private string _html = html;

        public QueryArrivalStore Store => host.Server.Services.GetRequiredService<QueryArrivalStore>();

        public static async Task<Arrival> Open(ITestOutputHelper output)
        {
            var host = RaskTestHost.Create<QueryArrivalApp>(
                configureServices: services =>
                {
                    services.AddRaskCqrs();
                    services.AddRaskQuery();
                    services.AddSingleton<QueryArrivalStore>();
                },
                diffMode: LiveDiffMode.DisabledFull);
            var html = await host.Http.GetStringAsync("/q/home");
            var sessionId = MarkupAssert.SessionId(html);
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            await ws.AttachedAsync(host, sessionId);
            return new Arrival(host, ws, output, html);
        }

        public Task Navigate(string path) => ws.SendJsonAsync(new { type = "navigate", path, query = "" });

        public Task Press(string buttonId)
        {
            var handler = Regex.Match(
                _html, $"id=\"{Regex.Escape(buttonId)}\"[^>]*data-rask-on-click=\"([^\"]+)\"",
                RegexOptions.None, TimeSpan.FromSeconds(1));
            return ws.SendJsonAsync(new { id = handler.Groups[1].Value, type = "click" });
        }

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

        public async Task<string> Until(string text)
        {
            string frame;
            do
            {
                frame = await Next();
            }
            while (!frame.Contains(text, StringComparison.Ordinal));

            return frame;
        }

        private void Note(string frame)
        {
            using var doc = JsonDocument.Parse(frame);
            if (doc.RootElement.TryGetProperty("html", out var page))
            {
                _html = page.GetString()!;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  frame {Encoding.UTF8.GetByteCount(frame)} B history={LiveFrames.HistoryUrl(frame)} spinner={frame.Contains("spinner", StringComparison.Ordinal)} rows={frame.Contains("order v", StringComparison.Ordinal)}"));
        }

        public async ValueTask DisposeAsync()
        {
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
