using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

// The documented save: `await thing.Save(); Routes.ListPage().Go();`. The list it lands on mounts inside the
// handler's turn, and loads under its own lifetime — not under the saving page's, which the navigation ends.
public sealed class ArrivalAfterHandlerTests
{
    private const string Loaded = "rows:3 total:3";

    [Theory]
    [InlineData("go-last")]
    [InlineData("go-then-await")]
    public async Task A_list_reached_by_Go_from_a_handler_shows_both_of_its_reads(string button)
    {
        await using var arrival = await Arrival.Open();

        await arrival.Click(button);

        await arrival.Shows(Loaded);
    }

    [Fact]
    public async Task A_list_reached_by_a_nav_link_shows_both_of_its_reads()
    {
        await using var arrival = await Arrival.Open();

        await arrival.Navigate("/arrival/list");

        await arrival.Shows(Loaded);
    }

    [Fact]
    public async Task A_layouts_handler_that_navigates_runs_on_to_its_end_and_the_list_loads()
    {
        await using var arrival = await Arrival.Open();

        await arrival.Click("keep");
        var frames = await arrival.Shows(Loaded, "kept:3");

        Assert.Contains(frames, f => f.Contains(Loaded, StringComparison.Ordinal));
        Assert.Contains(frames, f => f.Contains("kept:3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_handler_cancelled_by_its_own_navigation_still_lands_and_the_session_goes_on()
    {
        await using var arrival = await Arrival.Open();

        await arrival.Click("go-then-read");
        await arrival.Shows(Loaded);
        await arrival.Click("keep");

        await arrival.Shows("kept:3");
    }

    [Fact]
    public async Task A_first_request_for_a_page_that_navigates_while_it_mounts_is_sent_on_to_the_loaded_list()
    {
        using var host = RaskTestHost.Create<ArrivalApp>();

        var moved = await host.Http.GetAsync("/arrival/moved", TestContext.Current.CancellationToken);
        var list = await host.Http.GetStringAsync(moved.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Found, moved.StatusCode);
        Assert.Equal("/arrival/list", moved.Headers.Location?.OriginalString);
        Assert.Contains(Loaded, list);
    }

    private sealed class Arrival(RaskTestHost host, WebSocket ws, string html) : IAsyncDisposable
    {
        public static async Task<Arrival> Open()
        {
            var host = RaskTestHost.Create<ArrivalApp>();
            var html = await (await host.Http.GetAsync("/arrival/form")).Content.ReadAsStringAsync();
            var sessionId = MarkupAssert.SessionId(html);
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            await ws.AttachedAsync(host, sessionId);
            return new Arrival(host, ws, html);
        }

        // A handler id belongs to its component for the component's life, so the first page's ids still
        // address the layout's button after the page under it has changed.
        public Task Click(string buttonId)
        {
            var handler = Regex.Match(
                html, $"id=\"{Regex.Escape(buttonId)}\"[^>]*data-rask-on-click=\"([^\"]+)\"",
                RegexOptions.None, TimeSpan.FromSeconds(1));
            return ws.SendJsonAsync(new { id = handler.Groups[1].Value, type = "click" });
        }

        public Task Navigate(string path) => ws.SendJsonAsync(new { type = "navigate", path, query = "" });

        // The frames up to the one that completes the set: every text has been shown by then.
        public async Task<List<string>> Shows(params string[] texts)
        {
            var frames = new List<string>();
            await ws.ReceiveUntilAsync(
                frame =>
                {
                    frames.Add(frame);
                    return Array.TrueForAll(
                        texts, text => frames.Exists(f => f.Contains(text, StringComparison.Ordinal)));
                },
                $"frames showing {string.Join(" and ", texts)}");
            return frames;
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
