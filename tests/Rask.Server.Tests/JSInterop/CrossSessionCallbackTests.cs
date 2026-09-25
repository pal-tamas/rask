using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-helper Components predate framework-managed <head>

namespace Rask.Server.Tests.JSInterop;

// The browser answers a gesture, a watch or an observer by calling a static [JSInvokable] with an id. One
// Server process holds every session's ids, and they count up, so a socket that could post any id could feed
// another visitor's callback whatever it liked.
public class CrossSessionCallbackTests
{
    [Fact]
    public async Task A_socket_cannot_fire_another_session_s_gesture_callback_but_its_own_session_still_can()
    {
        using var host = RaskTestHost.Create<GestureApp>();
        var victimHtml = await host.Http.GetStringAsync("/");
        var attackerHtml = await host.Http.GetStringAsync("/");
        var victimRid = ResultId(victimHtml);
        using var victim = await Connect(host, victimHtml);
        using var attacker = await Connect(host, attackerHtml);

        await PostGestureResult(attacker, victimRid, "forged");
        await PostGestureResult(victim, victimRid, "#ff8800");

        Assert.Equal(["#ff8800"], GestureApp.Results.Where(r => r.Session == victimRid).Select(r => r.Value));
    }

    private static int ResultId(string html) => int.Parse(Regex.Match(html, @"rid&quot;:(\d+)").Groups[1].Value);

    private static async Task<System.Net.WebSockets.WebSocket> Connect(RaskTestHost host, string html)
    {
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = MarkupAssert.SessionId(html) });
        return ws;
    }

    // Waits for the server's dotNetResult, so the call has been dispatched before the next step.
    private static async Task PostGestureResult(System.Net.WebSockets.WebSocket ws, int rid, string value)
    {
        var callId = Guid.NewGuid().ToString("N");
        await ws.SendJsonAsync(new
        {
            type = "dotNetInvoke",
            callId,
            assemblyName = "Rask.Core",
            methodIdentifier = "RaskGestureResult",
            argsJson = $"[{rid},\"{value}\"]"
        });

        var reply = await ws.ReceiveUntilAsync(f => f.Contains(callId, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        Assert.NotNull(reply);
    }
}

internal sealed partial class GestureApp : Component
{
    public static ConcurrentQueue<(int Session, string? Value)> Results { get; } = new();

    protected override Component? HeadAssets => Markup.Title["t"];
    protected override string? HtmlLang => null;

    protected override Component? Render()
    {
        var rid = 0;
        return Trigger.EyeDropper
            .Template(g =>
            {
                rid = int.Parse(Regex.Match(g["rask-gesture"]!, @"""rid"":(\d+)").Groups[1].Value);
                return Button.Type("button").Data(g)["Pick"];
            })
            .OnColor(value =>
            {
                Results.Enqueue((rid, value));
                return Task.CompletedTask;
            });
    }
}
