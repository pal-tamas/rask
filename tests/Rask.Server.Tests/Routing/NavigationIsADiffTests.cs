using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

// What a navigation inside one layout costs on the wire, on the app the report came from (ListArrivalApp): a
// menu of eighty-four links, a header, and the page in the outlet. Only the outlet, the breadcrumb and the
// title change, so only they travel: the outlet as one morph of the <main> it is in, the title as the head
// that rides a diff. Each used to be the whole page, some 350 KB of it the menu.
[Collection("HostEnvironment")]
public sealed class NavigationIsADiffTests(ITestOutputHelper output)
{
    private const string LastRow = "Viszonylat 15";
    private const string Menu = "data-ui-sidebar-item";

    [Fact]
    public async Task A_navigation_between_two_pages_of_one_layout_sends_the_outlet_the_crumb_and_the_title_not_the_menu()
    {
        await using var session = await Session.Open(new ListArrivalStore(), "/destinations", output);

        await session.Navigate("/destinations/new");
        var frame = await session.Next();

        // 349,698 B as the whole page it used to be.
        Assert.StartsWith("{\"kind\":\"diff\"", frame, StringComparison.Ordinal);
        Assert.Contains("[8,[1,1,3],\"<", frame, StringComparison.Ordinal);
        Assert.DoesNotContain(Menu, frame, StringComparison.Ordinal);
        Assert.Contains("\"head\":\"<head", frame, StringComparison.Ordinal);
        Assert.Contains("&#xDA;j viszonylat | PPTA</title>", frame, StringComparison.Ordinal);
        Assert.Equal("/destinations/new", LiveFrames.HistoryUrl(frame));
        Assert.InRange(Encoding.UTF8.GetByteCount(frame), 1, 8_000);
    }

    [Fact]
    public async Task A_list_that_takes_the_place_of_its_loading_line_sends_the_main_it_is_in_not_the_menu()
    {
        var store = new ListArrivalStore();
        await using var session = await Session.Open(store, "/destinations/new", output);
        store.Hold();

        await session.Navigate("/destinations-loading");
        var loading = await session.Next();
        store.Release();
        var list = await session.Next();

        // 348,598 B and 417,357 B as the two whole pages they used to be.
        Assert.Contains("id=\\\"loading\\\"", loading, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(loading), 1, 4_000);
        Assert.StartsWith("{\"kind\":\"diff\",\"ops\":[[8,[1,1,3],", list, StringComparison.Ordinal);
        Assert.Contains(LastRow, list, StringComparison.Ordinal);
        Assert.DoesNotContain(Menu, list, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(list), 1, 72_000);
    }

    [Fact]
    public async Task A_form_that_takes_the_place_of_a_callout_sends_the_main_it_is_in_not_the_menu()
    {
        var store = new ListArrivalStore();
        await using var session = await Session.Open(store, "/destinations/new", output);
        store.Hold();

        await session.Navigate("/destinations/3");
        var missing = await session.Next();
        store.Release();
        var form = await session.Next();

        // 347,098 B and 348,394 B as the two whole pages they used to be.
        Assert.Contains("id=\\\"missing\\\"", missing, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(missing), 1, 4_000);
        Assert.StartsWith("{\"kind\":\"diff\"", form, StringComparison.Ordinal);
        Assert.Contains("Viszonylat 3 m", form, StringComparison.Ordinal);
        Assert.DoesNotContain(Menu, form, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(form), 1, 6_000);
    }

    [Fact]
    public async Task A_button_that_arrived_in_a_morphed_outlet_is_pressed_by_the_id_the_morph_brought_and_answered_with_diffs()
    {
        await using var session = await Session.Open(new ListArrivalStore(), "/destinations", output);
        await session.Navigate("/destinations/new");
        var arrived = await session.Next();
        var save = Regex.Match(
            arrived, "id=\\\\\"save\\\\\"[^>]*data-rask-on-click=\\\\\"([^\\\\\"]+)", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;

        await session.Press(save);
        var frames = await session.Until(LastRow);

        Assert.NotEmpty(save);
        Assert.All(frames, frame => Assert.StartsWith("{\"kind\":\"diff\"", frame, StringComparison.Ordinal));
        Assert.All(frames, frame => Assert.DoesNotContain(Menu, frame, StringComparison.Ordinal));
        Assert.Contains(LastRow, frames[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/trail/orders", "/trail/orders/new")]
    [InlineData("/trail/orders/new", "/trail/orders")]
    public async Task A_breadcrumb_item_that_turns_link_and_the_outlet_are_two_morphs_of_their_own_parents_not_one_of_the_layout(
        string from, string to)
    {
        await using var session = await Session.Open<TrailApp>(from, output);
        var whole = session.FirstBytes;

        await session.Navigate(to);
        var frame = await session.Next();

        var morphed = Regex.Matches(frame, "\\[8,\\[([0-9,]+)\\],", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value).ToArray();
        Assert.StartsWith("{\"kind\":\"diff\"", frame, StringComparison.Ordinal);
        Assert.Equal(["1,1,0,1,1,1", "1,1,0,2"], morphed);
        Assert.DoesNotContain(Menu, frame, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\\\"search\\\"", frame, StringComparison.Ordinal);
        Assert.Equal(to, LiveFrames.HistoryUrl(frame));
        Assert.InRange(Encoding.UTF8.GetByteCount(frame), 1, whole / 10);
    }

    // One page of the app over a live socket: where the reader goes, and each frame that comes back.
    private sealed class Session(RaskTestHost host, WebSocket ws, ITestOutputHelper output) : IAsyncDisposable
    {
        /// <summary>The size of the page as it was first served.</summary>
        public int FirstBytes { get; private init; }

        public static Task<Session> Open(ListArrivalStore store, string path, ITestOutputHelper output) =>
            Open<ListArrivalApp>(path, output, store);

        public static async Task<Session> Open<TApp>(string path, ITestOutputHelper output, ListArrivalStore? store = null)
            where TApp : Component
        {
            var host = RaskTestHost.Create<TApp>(
                configureServices: services => services.AddSingleton(store ?? new ListArrivalStore()), environment: "Development");
            var html = await host.Http.GetStringAsync(path);
            var sessionId = MarkupAssert.SessionId(html);
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            await ws.AttachedAsync(host, sessionId);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"GET {path}: {Encoding.UTF8.GetByteCount(html)} B"));
            return new Session(host, ws, output) { FirstBytes = Encoding.UTF8.GetByteCount(html) };
        }

        public Task Navigate(string path) => ws.SendJsonAsync(new { type = "navigate", path, query = "" });

        public Task Press(string handler) => ws.SendJsonAsync(new { id = handler, type = "click" });

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
