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

// A page that reads through Rask.Query where it renders, reached in place: the query's first fetch is work the
// navigation waits for, exactly as an awaited OnUpdated is, so the page arrives once with its rows. What is
// already in the session's cache is shown at once, and a query a save has just made out of date is waited for
// like one that has nothing to show — the reader who saved does not land on the rows from before their save.
public sealed class QueryArrivalFramesTests(ITestOutputHelper output)
{
    private const string Fresh = "order v1-3";

    // Longer than any wait here may last: a page is sent because its fetch came back, never because time ran out.
    private static void Patient(RaskServerOptions o) => o.NavigationQuiescenceTimeout = TimeSpan.FromMinutes(5);

    // Over while the test still holds the fetch.
    private static void Brief(RaskServerOptions o) => o.NavigationQuiescenceTimeout = TimeSpan.FromMilliseconds(40);

    [Fact]
    public async Task A_list_that_reads_through_a_query_reached_by_a_link_arrives_once_with_its_rows_and_no_spinner()
    {
        await using var arrival = await Arrival.Open(output, Patient);
        arrival.Store.Hold();

        await arrival.Navigate("/q/list");
        await arrival.Store.ReadStarted();
        arrival.Store.Release();
        var frame = Assert.Single(await arrival.Settled());

        Assert.Contains(Fresh, frame, StringComparison.Ordinal);
        Assert.DoesNotContain("spinner", frame, StringComparison.Ordinal);
        Assert.Equal("/q/list", LiveFrames.HistoryUrl(frame));
    }

    [Fact]
    public async Task A_list_that_reads_through_a_query_reached_by_Go_from_a_handler_arrives_once_with_its_rows()
    {
        await using var arrival = await Arrival.Open(output, Patient);
        arrival.Store.Hold();

        await arrival.Press("go");
        await arrival.Store.ReadStarted();
        arrival.Store.Release();
        var frame = Assert.Single(await arrival.Settled());

        Assert.Contains(Fresh, frame, StringComparison.Ordinal);
        Assert.DoesNotContain("spinner", frame, StringComparison.Ordinal);
        Assert.Equal("/q/list", LiveFrames.HistoryUrl(frame));
    }

    [Fact]
    public async Task A_query_whose_fetch_outlasts_the_budget_shows_its_spinner_at_the_budget_and_then_its_rows()
    {
        await using var arrival = await Arrival.Open(output, Brief);
        arrival.Store.Hold();

        await arrival.Navigate("/q/list");
        var atTheBudget = await arrival.Next();
        arrival.Store.Release();
        var rows = await arrival.Until(Fresh);

        Assert.Contains("spinner", atTheBudget, StringComparison.Ordinal);
        Assert.Equal("/q/list", LiveFrames.HistoryUrl(atTheBudget));
        Assert.Null(LiveFrames.HistoryUrl(rows));
    }

    [Fact]
    public async Task A_second_visit_to_a_list_whose_query_is_cached_is_one_frame_with_no_fetch_and_no_wait()
    {
        await using var arrival = await Arrival.Open(output, Patient);
        await arrival.Navigate("/q/list");
        await arrival.Settled();
        await arrival.Navigate("/q/home");
        await arrival.Settled();
        var readsBefore = arrival.Store.Reads;
        arrival.Store.Hold();

        await arrival.Navigate("/q/list");
        var frame = Assert.Single(await arrival.Settled());

        // The fetch is held, so a visit that fetched would wait out a budget longer than this test may run.
        Assert.Contains(Fresh, frame, StringComparison.Ordinal);
        Assert.Equal(readsBefore, arrival.Store.Reads);
    }

    [Fact]
    public async Task A_save_that_makes_a_cached_list_out_of_date_and_goes_to_it_lands_on_the_fresh_rows_in_one_frame()
    {
        await using var arrival = await Arrival.Open(output, Patient);
        await arrival.Navigate("/q/list");
        await arrival.Settled();
        await arrival.Navigate("/q/home");
        await arrival.Settled();
        arrival.Store.Hold();

        await arrival.Press("save");
        await arrival.Store.ReadStarted();
        arrival.Store.Release();
        var frame = Assert.Single(await arrival.Settled());

        // Not the rows from before the save, shown first and replaced: the reader saved, and sees what they saved.
        Assert.Contains("order v2-3", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("order v1-", frame, StringComparison.Ordinal);
        Assert.Equal("/q/list", LiveFrames.HistoryUrl(frame));
    }

    // Every frame is a whole page here, so the page a frame shows — and the handlers on it — can be read from it.
    private sealed class Arrival(RaskTestHost host, WebSocket ws, ITestOutputHelper output, string html) : IAsyncDisposable
    {
        private string _html = html;

        public QueryArrivalStore Store => host.Server.Services.GetRequiredService<QueryArrivalStore>();

        public static async Task<Arrival> Open(ITestOutputHelper output, Action<RaskServerOptions> configure)
        {
            var host = RaskTestHost.Create<QueryArrivalApp>(
                configureServices: services =>
                {
                    services.AddRaskCqrs();
                    services.AddRaskQuery();
                    services.AddSingleton<QueryArrivalStore>();
                },
                configureServer: configure,
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
