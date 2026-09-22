using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Server;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class StateHasChangedTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void StateHasChanged_without_a_handle_does_not_throw()
    {
        var c = new StubComponent(Span);
        c.StateHasChanged();
    }

    [Fact]
    public async Task StateHasChangedAsync_without_a_handle_gives_a_completed_task()
    {
        var c = new StubComponent(Span);
        await c.StateHasChangedAsync();
    }

    [Fact]
    public void A_component_gets_its_handle_from_the_session_on_construction()
    {
        var session = NewSession(out _);

        Assert.Same(session, session.View.RenderHandle);
    }

    [Fact]
    public async Task Requesting_a_render_with_no_socket_attached_does_nothing()
    {
        var session = NewSession(out _);
        await session.RequestRenderAsync();
        // Lock must remain free after the call.
        Assert.True(session.Lock.Wait(0));
        session.Lock.Release();
    }

    [Fact]
    public async Task Requesting_a_render_from_inside_a_handler_scope_does_not_acquire_the_lock()
    {
        var session = NewSession(out _);

        await session.Lock.WaitAsync();
        try
        {
            session.InHandlerScope = true;
            await session.RequestRenderAsync();
            Assert.True(session.InHandlerScope);
        }
        finally
        {
            session.InHandlerScope = false;
            session.Lock.Release();
        }
    }

    [Fact]
    public async Task Requesting_a_render_outside_a_handler_scope_leaves_the_lock_free()
    {
        var session = NewSession(out _);
        await session.RequestRenderAsync();
        Assert.True(session.Lock.Wait(0));
        session.Lock.Release();

        Assert.False(session.InHandlerScope);
    }

    [Fact]
    public void StateHasChanged_from_a_child_propagates_the_handle_via_the_live_render_context()
    {
        var session = NewSession(out _);
        var child = new StubComponent(Span);

        using (LiveRenderContext.Begin(session.View, session.Services))
        {
            var resolved = LiveRenderContext.Current!.GetOrCreate(_ => child);
            Assert.Same(session, resolved.RenderHandle);
        }
    }

    private static LiveSession NewSession(out IServiceScope scope)
    {
        var sp = RenderHarness.EmptyServices();
        scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return new LiveSession("test-session", new StubComponent(Span), scope, LiveDiffMode.Auto);
    }
}
