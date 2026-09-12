using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.Transport;

namespace Rask.Server.Tests.Live;

/// <summary>
///     Which connection a session is attached to, when two of them overlap.
/// </summary>
/// <remarks>
///     <para>
///         A tab that reconnects quickly runs two connections at once for a moment: the new one attaches
///         from its own <c>hello</c> while the old one is still unwinding through its <c>finally</c>. The
///         old cleanup cleared the session's connection unconditionally, so it detached the LIVE one —
///         renders stopped reaching a client that was sitting there connected, and the session was armed
///         for removal underneath it (#1076).
///     </para>
///     <para>
///         The same window is why the connected count is per CONNECTION rather than per attach: a resumed
///         session attached without ever counting, while every cleanup decremented, so the gauge drifted
///         below the truth over a restart (#1059).
///     </para>
/// </remarks>
public sealed class TransportAttachTests
{
    private sealed class Shell : Component
    {
        protected override string? HtmlLang => null;

        protected override Component? Render() => new H1()["hi"];
    }

    /// <summary>A connection that records what it was asked to carry.</summary>
    private sealed class FakeTransport : ILiveTransport
    {
        public bool IsOpen { get; set; } = true;

        public List<string> Sent { get; } = [];

        public int Aborts { get; private set; }

        public string? ClosedWith { get; private set; }

        public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Sent.Add(System.Text.Encoding.UTF8.GetString(frame.Span));
            return ValueTask.CompletedTask;
        }

        public Task CloseAsync(LiveTransportClose reason, string description, CancellationToken ct)
        {
            ClosedWith = description;
            IsOpen = false;
            return Task.CompletedTask;
        }

        public void Abort()
        {
            Aborts++;
            IsOpen = false;
        }
    }

    private static LiveSessionStore NewStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RaskServerLimits());
        var sp = services.BuildServiceProvider();
        return new LiveSessionStore(sp.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task An_older_connections_cleanup_leaves_the_newer_one_attached()
    {
        var session = NewStore().Create(_ => new Shell());
        var first = new FakeTransport();
        var second = new FakeTransport();

        session.AttachTransport(first, CancellationToken.None);
        session.AttachTransport(second, CancellationToken.None);

        // The first connection's loop unwinds here, after the reconnect has attached the second one.
        Assert.False(session.DetachTransport(first), "the first connection was no longer the attached one");

        await session.SendOutOfBandAsync("still here"u8.ToArray());

        Assert.Equal(["still here"], second.Sent);
        Assert.Empty(first.Sent);
    }

    [Fact]
    public async Task Detaching_the_attached_connection_reports_it_and_stops_the_sends()
    {
        var session = NewStore().Create(_ => new Shell());
        var transport = new FakeTransport();

        session.AttachTransport(transport, CancellationToken.None);

        Assert.True(session.DetachTransport(transport), "the attached connection detaches");
        Assert.False(session.DetachTransport(transport), "and only the first detach owns it");

        await session.SendOutOfBandAsync("nobody is listening"u8.ToArray());

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task A_closed_connection_is_not_written_to()
    {
        var session = NewStore().Create(_ => new Shell());
        var transport = new FakeTransport();

        session.AttachTransport(transport, CancellationToken.None);
        await transport.CloseAsync(LiveTransportClose.GoingAway, "server-shutdown", CancellationToken.None);

        await session.SendOutOfBandAsync("too late"u8.ToArray());

        Assert.Empty(transport.Sent);
    }

    /// <summary>
    ///     The shutdown announcement reaches the connection the session currently has, whichever it is.
    /// </summary>
    [Fact]
    public async Task The_shutdown_close_goes_to_the_attached_connection()
    {
        var session = NewStore().Create(_ => new Shell());
        var first = new FakeTransport();
        var second = new FakeTransport();

        session.AttachTransport(first, CancellationToken.None);
        session.AttachTransport(second, CancellationToken.None);

        await session.CloseForShutdownAsync(CancellationToken.None);

        Assert.Equal("server-shutdown", second.ClosedWith);
        Assert.Null(first.ClosedWith);
    }
}
