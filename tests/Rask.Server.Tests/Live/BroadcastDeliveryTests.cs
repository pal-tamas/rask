using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Messaging;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Live;

/// <summary>
///     #1061: a message published through <see cref="IBroadcast" /> reaches the subscribed component in every session
///     this server holds, runs on each session's own dispatch queue, and paints there.
/// </summary>
public sealed class BroadcastDeliveryTests
{
    [Fact]
    public async Task A_publish_repaints_every_connected_session()
    {
        using var host = RaskTestHost.Create<BroadcastApp>();
        await using var first = await ConnectAsync(host);
        await using var second = await ConnectAsync(host);

        await host.Services.GetRequiredService<IBroadcast>().PublishAsync(BroadcastApp.Headlines, "rain");

        Assert.Contains("seen=rain", await ReceiveUntilAsync(first.Ws, "seen=rain"), StringComparison.Ordinal);
        Assert.Contains("seen=rain", await ReceiveUntilAsync(second.Ws, "seen=rain"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delivery_keeps_its_place_among_the_sessions_events()
    {
        // The message and the click are dispatched in the order they were queued, under one lock, so neither loses
        // the other's state change.
        using var host = RaskTestHost.Create<BroadcastApp>();
        await using var client = await ConnectAsync(host);
        var broadcast = host.Services.GetRequiredService<IBroadcast>();

        await ClickAsync(client);
        await broadcast.PublishAsync(BroadcastApp.Headlines, "sun");
        await ClickAsync(client);

        Assert.Contains("seen=sun;clicks=2", await ReceiveUntilAsync(client.Ws, "seen=sun;clicks=2"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_that_is_not_connected_applies_the_message_and_shows_it_when_it_attaches()
    {
        using var host = RaskTestHost.Create<BroadcastApp>();
        var sessionId = MarkupAssert.SessionId(await host.Http.GetStringAsync("/start"));
        var session = host.Store.Get(sessionId)!;

        await host.Services.GetRequiredService<IBroadcast>().PublishAsync(BroadcastApp.Headlines, "snow");
        await session.LastHandlerTask.WaitAsync(TimeSpan.FromSeconds(5));

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        Assert.Contains("seen=snow", await ReceiveUntilAsync(ws, "seen=snow"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_full_queue_skips_the_session_and_leaves_its_socket_open()
    {
        using var host = RaskTestHost.Create<BroadcastApp>(configureServer: o => o.MaxPendingHandlers = 1);
        await using var client = await ConnectAsync(host);
        var broadcast = host.Services.GetRequiredService<IBroadcast>();

        // Hold the session's dispatch lock so deliveries queue behind it instead of draining.
        await client.Session.Lock.WaitAsync();
        try
        {
            for (var i = 0; i < 5; i++)
            {
                await broadcast.PublishAsync(BroadcastApp.Headlines, $"m{i}");
            }

            Assert.True(client.Session.PendingHandlers <= 1, $"{client.Session.PendingHandlers} dispatches were queued past the limit");
        }
        finally
        {
            client.Session.Lock.Release();
        }

        // The one that fit is delivered, and the socket is still the same, open one.
        Assert.Contains("seen=m0", await ReceiveUntilAsync(client.Ws, "seen=m0"), StringComparison.Ordinal);
        Assert.Equal(WebSocketState.Open, client.Ws.State);
    }

    [Fact]
    public async Task A_render_requested_while_the_scope_is_held_is_rendered_when_it_is_released()
    {
        // A background StateHasChanged that lands after a dispatch's render loop has settled, but before the scope is
        // cleared, only parks itself on the pending flag. The dispatch side of the handoff renders it once the scope is
        // released; before, it waited for the next event, and a broadcast into an idle page showed nothing.
        using var host = RaskTestHost.Create<BroadcastApp>();
        await using var client = await ConnectAsync(host);
        var app = BroadcastApp.Last!;

        await client.Session.Lock.WaitAsync();
        client.Session.InHandlerScope = true;
        var before = app.RenderCount;
        app.StateHasChanged(); // parks: the scope is held
        client.Session.InHandlerScope = false;
        client.Session.Lock.Release();
        Assert.Equal(before, app.RenderCount);

        await client.Session.DrainRenderRequestedAfterScope();

        Assert.True(app.RenderCount > before, "the parked render request was dropped");
    }

    private static async Task<ConnectedClient> ConnectAsync(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/start");
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        // Wait for the attach itself, not for a frame: a hello with nothing to catch up on sends none, and a test that
        // raced ahead of the attach would see a detached session, where a render request only marks the catch-up.
        var session = host.Store.Get(sessionId)!;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!session.HasOpenTransport)
        {
            Assert.True(DateTime.UtcNow < deadline, "the socket never attached");
            await Task.Delay(5);
        }

        return new ConnectedClient(ws, session, MarkupAssert.FirstHandlerId(html));
    }

    private static Task ClickAsync(ConnectedClient client) => client.Ws.SendJsonAsync(new { id = client.BumpHandler });

    private static async Task<string> ReceiveUntilAsync(WebSocket ws, string needle)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var seen = new System.Text.StringBuilder();
        while (DateTime.UtcNow < deadline)
        {
            var frame = await ws.TryReceiveTextAsync(deadline - DateTime.UtcNow);
            if (frame is null)
            {
                break;
            }

            seen.Append(frame);
            if (frame.Contains(needle, StringComparison.Ordinal))
            {
                return frame;
            }
        }

        return seen.ToString();
    }

    private sealed record ConnectedClient(WebSocket Ws, LiveSession Session, string BumpHandler) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Ws.State == WebSocketState.Open)
                {
                    await Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
            }

            Ws.Dispose();
        }
    }
}
