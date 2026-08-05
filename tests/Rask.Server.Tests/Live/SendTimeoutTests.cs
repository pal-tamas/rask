using System.Diagnostics;
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Live;

/// <summary>
/// A client that stops reading must not be able to pin its session forever.
/// </summary>
/// <remarks>
/// <c>WebSocket.SendAsync</c> completes when the frame reaches the transport, not when the client reads
/// it, so a client that simply stops reading fills the send buffer and the send never returns. Every send
/// happens under the session's render lock, which also guards its teardown — so without a bound, one
/// unresponsive client costs its session every future render and a <c>Dispose</c> that can never take the
/// lock. The stalling socket below is that client, made deterministic.
/// </remarks>
public sealed class SendTimeoutTests
{
    private sealed class Shell : Component
    {
        protected override Component? Render() =>
            [Doctype(), new Html()[new Head(), new Body()[new H1()["hi"]]]];
    }

    private static LiveSessionStore NewStore(TimeSpan sendTimeout)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RaskServerLimits { SendTimeout = sendTimeout });
        var sp = services.BuildServiceProvider();
        return new LiveSessionStore(sp.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task A_send_that_never_completes_is_abandoned_and_the_socket_aborted()
    {
        var store = NewStore(TimeSpan.FromMilliseconds(200));
        var session = store.Create(_ => new Shell());
        var socket = new StallingWebSocket();
        session.AttachSocket(socket, CancellationToken.None);

        var started = Stopwatch.GetTimestamp();
        await Assert.ThrowsAsync<WebSocketException>(
            () => session.SendOutOfBandAsync("hello"u8.ToArray()));

        Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5), "the send must not have waited out the test");
        Assert.Equal(1, socket.Aborted);
    }

    /// <summary>
    /// The point of the abort: the render lock comes back. Without it the session is not merely slow, it is
    /// permanently wedged — nothing can render and nothing can dispose it.
    /// </summary>
    [Fact]
    public async Task The_session_is_usable_again_after_a_send_times_out()
    {
        var store = NewStore(TimeSpan.FromMilliseconds(200));
        var session = store.Create(_ => new Shell());
        session.AttachSocket(new StallingWebSocket(), CancellationToken.None);

        await Assert.ThrowsAsync<WebSocketException>(
            () => session.SendOutOfBandAsync("hello"u8.ToArray()));

        // Dispose takes the same lock the wedged send was holding. If it returns, the lock was released.
        var disposed = Task.Run(() => session.Dispose());
        Assert.True(await Task.WhenAny(disposed, Task.Delay(TimeSpan.FromSeconds(5))) == disposed,
            "Dispose must not block on a lock the timed-out send still holds");
        await disposed;
    }

    /// <summary>A send that completes normally must be untouched by any of this.</summary>
    [Fact]
    public async Task A_healthy_send_is_unaffected()
    {
        var store = NewStore(TimeSpan.FromSeconds(30));
        var session = store.Create(_ => new Shell());
        var socket = new StallingWebSocket();
        session.AttachSocket(socket, CancellationToken.None);

        socket.Release();
        await session.SendOutOfBandAsync("hello"u8.ToArray());

        Assert.Equal(0, socket.Aborted);
    }

    /// <summary>Zero restores the prior unbounded behaviour for anyone who needs it back.</summary>
    [Fact]
    public async Task A_zero_timeout_does_not_arm_the_bound()
    {
        var store = NewStore(TimeSpan.Zero);
        var session = store.Create(_ => new Shell());
        var socket = new StallingWebSocket();
        session.AttachSocket(socket, CancellationToken.None);

        var send = session.SendOutOfBandAsync("hello"u8.ToArray());
        var finished = await Task.WhenAny(send, Task.Delay(TimeSpan.FromMilliseconds(400)));

        Assert.NotSame(send, finished);
        Assert.Equal(0, socket.Aborted);

        socket.Release();
        await send;
    }

    /// <summary>
    /// One stalled session must not hold up delivery to the rest. Sequentially this took the sum of every
    /// session's timeout; concurrently it takes roughly one.
    /// </summary>
    [Fact]
    public async Task A_broadcast_is_not_held_up_by_one_stalled_session()
    {
        var store = NewStore(TimeSpan.FromMilliseconds(300));
        var sockets = new List<StallingWebSocket>();
        for (var i = 0; i < 6; i++)
        {
            var session = store.Create(_ => new Shell());
            var socket = new StallingWebSocket();
            session.AttachSocket(socket, CancellationToken.None);
            sockets.Add(socket);
        }

        var started = Stopwatch.GetTimestamp();
        await store.BroadcastAsync("hello"u8.ToArray());
        var elapsed = Stopwatch.GetElapsedTime(started);

        // Six sessions × 300 ms is 1.8 s sequentially. Concurrently it is one timeout plus scheduling.
        Assert.True(elapsed < TimeSpan.FromSeconds(1),
            $"broadcast took {elapsed.TotalMilliseconds:F0} ms — it is serialising on the stalled sessions");
        Assert.All(sockets, s => Assert.Equal(1, s.Aborted));
    }
}
