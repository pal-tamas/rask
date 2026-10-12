using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// A callout written ahead of two stateful components used to rebuild both every time it came or went: a
// component was told apart by its place among ALL its owner's children, and the callout took one. Over a
// socket that is a list that loads again and a count that goes back to nought, in the middle of a session.
public class ConditionalSiblingStateTests
{
    [Fact]
    public async Task A_callout_that_appears_above_a_list_and_a_count_leaves_what_they_hold()
    {
        var loads = new ListLoads();
        using var host = RaskTestHost.Create<CalloutAboveApp>(s => s.AddSingleton(loads));
        var (ws, html) = await Connect(host);
        using var socket = ws;
        var pressed = await Click(ws, html, "press");

        var shown = await Click(ws, html, "save");
        var hidden = await Click(ws, html, "save");
        var pressedAgain = await Click(ws, html, "press");

        Assert.Contains("load 1", html);
        Assert.Contains("pressed 1", pressed);
        Assert.Contains("Saved", shown);
        Assert.DoesNotContain("pressed 0", shown);
        Assert.DoesNotContain("pressed 0", hidden);
        Assert.Contains("pressed 2", pressedAgain);
        Assert.Equal(1, loads.Count);
    }

    // The page as the browser holds it, with the hello-time catch-up (if the session owed one) already read.
    private static async Task<(WebSocket Ws, string Html)> Connect(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = MarkupAssert.SessionId(html) }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();
        return (ws, html);
    }

    private static async Task<string> Click(WebSocket ws, string html, string buttonId)
    {
        var handler = Regex.Match(html, $"id=\"{buttonId}\" data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual(string.Empty, handler);
        await ws.SendJsonAsync(new { id = handler }, ct: TestContext.Current.CancellationToken);
        return await ws.ReceiveTextAsync();
    }
}
