using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

// What a navigation inside one layout costs on the wire, on the app the report came from (ListArrivalApp): a
// menu of eighty-four links, a header, and the page in the outlet. Only the outlet, the breadcrumb and the
// title change, so only they should travel.
[Collection("HostEnvironment")]
public sealed class NavigationIsADiffTests(ITestOutputHelper output)
{
    private const string LastRow = "Viszonylat 15";
    private const string Menu = "data-ui-sidebar-item";

    [Fact]
    public async Task A_navigation_between_two_pages_of_one_layout_sends_the_whole_page_again()
    {
        await using var session = await Session.Open(new ListArrivalStore(), "/destinations", output);

        await session.Navigate("/destinations/new");
        var frame = await session.Next();

        Assert.StartsWith("{\"html\":", frame, StringComparison.Ordinal);
        Assert.Contains(Menu, frame, StringComparison.Ordinal);
        Assert.Equal("/destinations/new", LiveFrames.HistoryUrl(frame));
    }

    [Fact]
    public async Task A_list_that_takes_the_place_of_its_loading_line_sends_the_whole_page_again()
    {
        var store = new ListArrivalStore();
        await using var session = await Session.Open(store, "/destinations/new", output);
        store.Hold();

        await session.Navigate("/destinations-loading");
        var loading = await session.Next();
        store.Release();
        var list = await session.Next();

        Assert.Contains("id=\\\"loading\\\"", loading, StringComparison.Ordinal);
        Assert.StartsWith("{\"html\":", list, StringComparison.Ordinal);
        Assert.Contains(LastRow, list, StringComparison.Ordinal);
        Assert.Contains(Menu, list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_form_that_takes_the_place_of_a_callout_sends_the_whole_page_again()
    {
        var store = new ListArrivalStore();
        await using var session = await Session.Open(store, "/destinations/new", output);
        store.Hold();

        await session.Navigate("/destinations/3");
        var missing = await session.Next();
        store.Release();
        var form = await session.Next();

        Assert.Contains("id=\\\"missing\\\"", missing, StringComparison.Ordinal);
        Assert.StartsWith("{\"html\":", form, StringComparison.Ordinal);
        Assert.Contains("Viszonylat 3 m", form, StringComparison.Ordinal);
        Assert.Contains(Menu, form, StringComparison.Ordinal);
    }

    // One page of the app over a live socket: where the reader goes, and each frame that comes back.
    private sealed class Session(RaskTestHost host, WebSocket ws, ITestOutputHelper output) : IAsyncDisposable
    {
        public static async Task<Session> Open(ListArrivalStore store, string path, ITestOutputHelper output)
        {
            var host = RaskTestHost.Create<ListArrivalApp>(
                configureServices: services => services.AddSingleton(store), environment: "Development");
            var html = await host.Http.GetStringAsync(path);
            var sessionId = MarkupAssert.SessionId(html);
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            await ws.AttachedAsync(host, sessionId);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"GET {path}: {Encoding.UTF8.GetByteCount(html)} B"));
            return new Session(host, ws, output);
        }

        public Task Navigate(string path) => ws.SendJsonAsync(new { type = "navigate", path, query = "" });

        public async Task<string> Next()
        {
            var frame = await ws.ReceiveUntilAsync(static f => !f.StartsWith("{\"type\":\"ack\"", StringComparison.Ordinal), "a frame");
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {(frame.StartsWith("{\"html\":", StringComparison.Ordinal) ? "page" : "diff")} {Encoding.UTF8.GetByteCount(frame)} B history={LiveFrames.HistoryUrl(frame)}"));
            return frame;
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
