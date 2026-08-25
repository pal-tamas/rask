using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Chrome.Components;
using Rask.Core;
using Rask.Html.Components;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     A server app inside a native shell describes its bars to the shell instead of drawing them, and a
///     press on one of those bars runs the app's own callback.
/// </summary>
/// <remarks>
///     Exercised over the real WebSocket, not against the session object, because the delivery is the part
///     that was hard: the document is rendered on the HTTP GET, while a queued JS invoke can only ride a
///     frame. The first descriptor is therefore built before any transport exists — and if nothing re-sends
///     it at the handshake the app boots with no bars at all, which is a failure no session-level assertion
///     would have caught.
/// </remarks>
public class NativeChromeDeliveryTests
{
    /// <summary>
    ///     Pull a bar item's id out of the descriptor the shell was actually handed.
    /// </summary>
    /// <remarks>
    ///     Read back rather than assumed. An id this test invented would pass while the real bridge sent
    ///     something else entirely, which is precisely the bug worth catching. Doubly escaped because the
    ///     descriptor is JSON inside the frame's own JSON.
    /// </remarks>
    private static string BarItemId(string frame)
    {
        using var payload = JsonDocument.Parse(frame);
        // The descriptor is JSON inside a JSON string argument inside the frame's JSON. Parsed rather than
        // pattern-matched, so this test breaks on a changed CONTRACT and not on a changed escaping.
        var args = payload.RootElement.GetProperty("jsInvokes")[0].GetProperty("argsJson").GetString()!;
        using var argsDoc = JsonDocument.Parse(args);
        using var descriptor = JsonDocument.Parse(argsDoc.RootElement[0].GetString()!);

        return descriptor.RootElement
            .GetProperty("header").GetProperty("trailing")[0].GetProperty("id").GetString()!;
    }

    private static async Task<(RaskTestHost Host, string Session)> ConnectAsync<TApp>(bool native)
        where TApp : Component, new()
    {
        var host = RaskTestHost.Create<TApp>();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        if (native)
        {
            request.Headers.Add("X-Rask-Shell", "native");
        }

        using var response = await host.Http.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        return (host, Regex.Match(html, "data-rask-root=\"([^\"]+)\"").Groups[1].Value);
    }

    [Fact]
    public async Task The_shell_is_sent_the_bars_as_soon_as_it_connects()
    {
        var (host, session) = await ConnectAsync<TapBarApp>(native: true);
        using var owned = host;

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session });
        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.False(string.IsNullOrEmpty(frame), "the handshake sent no frame at all");
        // The descriptor, not markup: the shell is being told what to draw.
        Assert.Contains("__raskNative.applyChrome", frame!, StringComparison.Ordinal);
        Assert.Contains("Inbox", frame!, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The control. An ordinary browser session must not be handed a descriptor — it renders the bars as
    ///     HTML, and a stray applyChrome would be a call into a bridge that does not exist there.
    /// </summary>
    [Fact]
    public async Task An_ordinary_browser_session_is_sent_no_descriptor()
    {
        var (host, session) = await ConnectAsync<TapBarApp>(native: false);
        using var owned = host;

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session });
        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(1));

        Assert.DoesNotContain("applyChrome", frame ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A press on a platform bar reaches the app's <c>OnClick</c> — the callback lives on the server, so
    ///     the tap has to travel back, and the resulting change has to reach the page.
    /// </summary>
    [Fact]
    public async Task A_tap_on_a_platform_bar_runs_the_apps_callback()
    {
        var (host, session) = await ConnectAsync<TapBarApp>(native: true);
        using var owned = host;

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session });
        var chrome = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.False(string.IsNullOrEmpty(chrome));

        var id = BarItemId(chrome!);
        Assert.False(string.IsNullOrEmpty(id), $"no bar item id in the descriptor: {chrome}");

        await ws.SendJsonAsync(new { type = "nativeTap", id });
        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.False(string.IsNullOrEmpty(frame), "the tap produced no frame");
        Assert.Contains("tapped=1", frame!, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An id from a bar that has since been replaced does nothing, rather than throwing or taking the
    ///     socket down. The press raced the swap; that is normal, not an error.
    /// </summary>
    [Fact]
    public async Task An_unknown_tap_id_is_ignored_and_the_session_survives()
    {
        var (host, session) = await ConnectAsync<TapBarApp>(native: true);
        using var owned = host;

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session });
        var chrome = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.False(string.IsNullOrEmpty(chrome), "the handshake sent no descriptor");

        await ws.SendJsonAsync(new { type = "nativeTap", id = "not-a-real-id" });
        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
        Assert.True(string.IsNullOrEmpty(frame), $"an unknown tap rendered anyway: {frame}");

        // Still alive: the real id still works after the unknown one was ignored.
        var id = BarItemId(chrome!);
        await ws.SendJsonAsync(new { type = "nativeTap", id });
        var after = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("tapped=1", after ?? string.Empty, StringComparison.Ordinal);
    }
}

/// <summary>
///     A server app with a portable bar whose button mutates state. It names no native type — the same
///     component serves a browser and a native shell, which is the whole point of the portable bars.
/// </summary>
internal sealed partial class TapBarApp : Component
{
    private int _taps;

    protected override Component? Render() =>
    [
        AppBar.Title("Inbox").Trailing([BarButton.Icon(BarIcon.Add).Title("Add").OnClick(() => _taps++)]),
        Div[$"tapped={_taps}"],
    ];
}
