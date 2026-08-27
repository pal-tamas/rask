using System.Net.WebSockets;
using System.Text.Json;
using Rask.Core;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class HelloMessageTests
{
    [Fact]
    public async Task Hello_UnknownSessionId_SendsSessionUnknownPayload()
    {
        using var host = RaskTestHost.Create<TestApp>();
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);

        await ws.SendJsonAsync(new { type = "hello", session = "no-such-id" });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        Assert.Equal("session", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("unknown", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Hello_NothingPendingAfterGet_SuppressesHelloTimeFrame()
    {
        // Updated contract: when nothing happened between the HTTP GET render and the WS
        // hello (no dropped StateHasChanged, no queued JS invokes), the browser already has
        // the current HTML from the GET response and a hello-time render would just re-fire
        // OnRendered on every alive component for no visible change. The first WS frame is
        // emitted lazily by the next real state mutation or event. Aligns Server's initial-
        // mount behaviour with WASM's (which has no analogous GET→hello handoff phase).
        using var host = RaskTestHost.Create<TestApp>();
        var initial = await host.Http.GetAsync("/start");
        var sessionId = MarkupAssert.SessionId(await initial.Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
        Assert.Null(text);
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task Hello_StateMutatedBeforeHello_EmitsCatchUpFrame()
    {
        // Counterpart to Hello_NothingPendingAfterGet_SuppressesHelloTimeFrame: when a
        // StateHasChanged WAS issued during the GET→hello handoff window, the hello-time render
        // must emit so the browser picks up the post-GET state.
        //
        // The window used to be opened by an OnMountAsync continuation. It no longer can be — the
        // GET awaits that work now and serves the result, which is the whole point of quiescence.
        // So the window is opened here the way it still genuinely occurs in production: work the
        // GET deliberately does NOT wait for, detached from the hook and pushing later. Rask's own
        // PollingPanel is exactly this shape.
        using var host = RaskTestHost.Create<DetachedPushApp>();
        var sessionId = MarkupAssert.SessionId(await host.Http.GetStringAsync("/start"));

        // Let MountAsyncApp's OnMountAsync await complete before opening the socket.
        // The continuation calls StateHasChanged with no socket attached, setting the
        // session's pending-render flag.
        await DetachedPushApp.Pushed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(text);
        // The catch-up frame must carry the post-GET "loaded" state. Its wire shape depends
        // on the active diff mode: with the diff codec on, the GET render seeded the baseline
        // (<p>loading</p>) so this ships a minimal text diff (loading -> loaded) the browser
        // applies against the GET HTML; under DisabledFull it ships full HTML. Either way the
        // new text must reach the browser, which is what this test guards.
        Assert.Contains("loaded", text);
    }

    [Fact]
    public async Task Hello_Reconnect_AlwaysEmitsFrame_EvenWithIdenticalHtml()
    {
        // The first-attach optimisation does NOT apply to reconnects: a tab that lost
        // its socket may have missed the prior socket's last frame to a partial send or
        // some transport gap we can't observe. Always render on reconnect so the new
        // socket's browser tab reliably gets the current state, even when the HTML
        // matches the seeded GET-time baseline byte-for-byte.
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = MarkupAssert.SessionId(await host.Http.GetStringAsync("/start"));

        var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws1.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws1.TryReceiveTextAsync(TimeSpan.FromMilliseconds(200));
        await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye",
            CancellationToken.None);

        using var ws2 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId });
        var frame = await ws2.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame!);
        Assert.True(doc.RootElement.TryGetProperty("html", out _));
    }

    [Fact]
    public async Task Hello_MissingSessionField_ConnectionStaysOpen_NoPayload()
    {
        using var host = RaskTestHost.Create<TestApp>();
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);

        await ws.SendJsonAsync(new { type = "hello" });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(300));

        Assert.Null(text);
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    // Component whose OnMountAsync flips state via StateHasChanged shortly after the GET
    // render completes — exercises the "drop happened before hello" branch of
    // FlushPendingRenderAsync.
#pragma warning disable RASK019 // test-helper Components predate framework-managed <head>
    // Pushes state AFTER the response, from work detached from the lifecycle hook. OnMountAsync
    // returns immediately, so the GET's quiescence wait settles at once and serves "loading"; the
    // detached continuation then lands in the GET→hello window with no socket attached.
    private sealed class DetachedPushApp : Component
    {
        public static readonly TaskCompletionSource Pushed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _loaded;

        protected override Task OnMountAsync()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                _loaded = true;
                StateHasChanged();
                Pushed.TrySetResult();
            });

            return Task.CompletedTask;
        }

        protected override Component? HeadAssets => Title["detached-push-app"];
        protected override string? HtmlLang => null;

        protected override Component? Render() => P[_loaded ? "loaded" : "loading"];
    }
#pragma warning restore RASK019
}
