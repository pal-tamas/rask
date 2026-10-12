using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Live;

/// <summary>
///     A session is disposed under work that is still running: a handler the shutdown drain gave up on, a render
///     queued behind it. None of that work may be left waiting for good, and none of it is a fault.
/// </summary>
/// <remarks>
///     The socket here stays open after the session has gone, as a real one does for a moment: its loop detaches
///     it only once it has unwound, and the drain does not wait for that past its budget.
/// </remarks>
public sealed class DisposedSessionTests
{
    [Fact]
    public async Task The_repaint_of_a_handler_still_running_is_nothing_once_its_session_is_disposed()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var session = host.Store.Create(sp => new TestApp(sp.GetRequiredService<RouteState>()));
        session.AttachSocket(OpenSocket(), CancellationToken.None);
        await host.Store.RemoveAsync(session.Id);

        var repaint = ((IRenderHandle)session).RenderInScopeAsync();

        await repaint.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_repaint_of_a_handler_still_running_is_nothing_once_its_connection_is_closed()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var session = host.Store.Create(sp => new TestApp(sp.GetRequiredService<RouteState>()));
        using var connection = new CancellationTokenSource();
        session.AttachSocket(OpenSocket(), connection.Token);
        await connection.CancelAsync();

        var repaint = ((IRenderHandle)session).RenderInScopeAsync();

        await repaint.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_render_waiting_behind_a_handler_ends_when_the_handler_does_though_the_session_was_disposed()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var session = host.Store.Create(sp => new TestApp(sp.GetRequiredService<RouteState>()));
        await session.Lock.WaitAsync(TestContext.Current.CancellationToken);
        var waiting = session.RenderAfterReadsAsync();
        await host.Store.RemoveAsync(session.Id);

        session.Lock.Release();

        await waiting.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_render_that_waited_out_the_teardown_renders_nothing_of_the_disposed_page()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var page = new SlowToLeave();
        var session = host.Store.Create(_ => page);
        session.AttachSocket(OpenSocket(), CancellationToken.None);
        var disposing = host.Store.RemoveAsync(session.Id);
        var render = session.RenderAndSendAsync(null, false);

        page.Leave.SetResult();
        await disposing.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);
        var sent = await render.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Equal(0, page.Renders);
    }

    private static StallingWebSocket OpenSocket()
    {
        var socket = new StallingWebSocket();
        socket.Release();
        return socket;
    }

    // Holds the session's teardown open, so a render can be queued behind it.
    private sealed class SlowToLeave : Component, IAsyncDisposable
    {
        public TaskCompletionSource Leave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Renders { get; private set; }

        protected override string? HtmlLang => null;

        public async ValueTask DisposeAsync() => await Leave.Task.ConfigureAwait(false);

        protected override Component? Render()
        {
            Renders++;
            return Markup.H1["leaving"];
        }
    }
}
