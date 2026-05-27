using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        var sessionId = ExtractSessionId(await initial.Content.ReadAsStringAsync());

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
        // StateHasChanged WAS issued during the GET→hello handoff window (typically an
        // async OnMountAsync continuation that completed before the WS attached), the
        // hello-time render must emit so the browser picks up the post-GET state.
        using var host = RaskTestHost.Create<MountAsyncApp>();
        var sessionId = ExtractSessionId(await host.Http.GetStringAsync("/start"));

        // Let MountAsyncApp's OnMountAsync await complete before opening the socket.
        // The continuation calls StateHasChanged with no socket attached, setting the
        // session's pending-render flag.
        await MountAsyncApp.AwaitCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        Assert.True(doc.RootElement.TryGetProperty("html", out var htmlProp));
        Assert.Contains("loaded", htmlProp.GetString());
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
        var sessionId = ExtractSessionId(await host.Http.GetStringAsync("/start"));

        var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws1.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws1.TryReceiveTextAsync(TimeSpan.FromMilliseconds(200));
        await ws1.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye",
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

    private static string ExtractSessionId(string html)
    {
        var match = Regex.Match(html, "data-rask-root=\"([^\"]+)\"");
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    // Component whose OnMountAsync flips state via StateHasChanged shortly after the GET
    // render completes — exercises the "drop happened before hello" branch of
    // FlushPendingRenderAsync.
#pragma warning disable RASK019 // test-helper Components predate framework-managed <head>
    private sealed class MountAsyncApp : Rask.Core.Component
    {
        public static readonly TaskCompletionSource AwaitCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _loaded;

        protected override async Task OnMountAsync()
        {
            await Task.Delay(50);
            _loaded = true;
            StateHasChanged();
            AwaitCompleted.TrySetResult();
        }

        protected override Rask.Core.RenderResult Render() =>
            Rask.Core.Components.Components.Fragment()[
                Rask.Core.Components.Components.Doctype(),
                Rask.Core.Components.Components.Html()[
                    Rask.Core.Components.Components.Head()[
                        Rask.Core.Components.Components.Title()["mount-async-app"]],
                    Rask.Core.Components.Components.Body()[
                        Rask.Core.Components.Components.P()[_loaded ? "loaded" : "loading"]]]];
    }
#pragma warning restore RASK019
}
