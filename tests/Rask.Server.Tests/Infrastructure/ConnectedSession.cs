using System.Net.WebSockets;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A connected live session for WebSocket tests: hosts <typeparamref name="TApp" />, GETs the
///     shell, opens the socket, sends <c>hello</c>, and drains the post-attach catch-up frame so
///     per-test assertions observe only the traffic they trigger. The GET render already seeded
///     the diff/head baseline, so the first interaction a test triggers diffs against it.
///     Disposes the socket and host on teardown. Consolidates the per-file copies.
/// </summary>
internal sealed class ConnectedSession : IAsyncDisposable
{
    private ConnectedSession(RaskTestHost host, WebSocket ws, LiveSession session)
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
        catch
        {
        }

        Ws.Dispose();
        Host.Dispose();
    }

    public static async Task<ConnectedSession> Connect<TApp>(LiveDiffMode diffMode = LiveDiffMode.Auto)
        where TApp : Component
    {
        var host = RaskTestHost.Create<TApp>(diffMode: diffMode);
        var initial = await host.Http.GetAsync("/start");
        var sessionId = Markup.SessionId(await initial.Content.ReadAsStringAsync());
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        var session = host.Store.Get(sessionId)!;
        return new ConnectedSession(host, ws, session);
    }
}
