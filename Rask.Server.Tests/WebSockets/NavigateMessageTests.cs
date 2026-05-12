using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class NavigateMessageTests
{
    [Fact]
    public async Task Navigate_UpdatesRouteState_AndSendsPayloadWithHistoryPush()
    {
        await using var fixture = await Connect();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/destination", query = "" });

        var text = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("push", history.GetProperty("action").GetString());
        Assert.Equal("/destination", history.GetProperty("url").GetString());

        var routeState = fixture.Session.Services.GetRequiredService<RouteState>();
        Assert.Equal("/destination", routeState.Path);
    }

    [Fact]
    public async Task Navigate_WithReplaceTrue_SendsHistoryReplace()
    {
        await using var fixture = await Connect();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/x", query = "", replace = true });

        var text = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        using var doc = JsonDocument.Parse(text!);
        Assert.Equal("replace", doc.RootElement.GetProperty("history").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Navigate_EmptyPath_NoPayload()
    {
        await using var fixture = await Connect();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "" });
        var text = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(300));

        Assert.Null(text);
    }

    [Fact]
    public async Task Navigate_QueryWithoutLeadingQuestion_NormalisesUrl()
    {
        await using var fixture = await Connect();

        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/x", query = "a=1&b=2" });

        var text = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        using var doc = JsonDocument.Parse(text!);
        Assert.Equal("/x?a=1&b=2", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
    }

    private static async Task<ConnectedSession> Connect()
    {
        var host = RaskTestHost.Create<TestApp>();
        var initial = await host.Http.GetAsync("/start");
        var sessionId = ExtractSessionId(await initial.Content.ReadAsStringAsync());
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        // Drain the post-attach refresh render
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        var session = host.Store.Get(sessionId)!;
        return new ConnectedSession(host, ws, session);
    }

    private static string ExtractSessionId(string html) =>
        Regex.Match(html, "data-rask-root=\"([^\"]+)\"").Groups[1].Value;

    private sealed class ConnectedSession : IAsyncDisposable
    {
        public ConnectedSession(RaskTestHost host, WebSocket ws, LiveSession session)
        {
            Host = host;
            Ws = ws;
            Session = session;
        }

        public RaskTestHost Host { get; }
        public WebSocket Ws { get; }
        public LiveSession Session { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Ws.State == WebSocketState.Open)
                {
                    await Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            }
            catch { }

            Ws.Dispose();
            Host.Dispose();
        }
    }
}
