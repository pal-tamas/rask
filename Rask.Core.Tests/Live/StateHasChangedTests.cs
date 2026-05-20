using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Server;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public class StateHasChangedTests
{
    [Fact]
    public void StateHasChanged_NoHandle_DoesNotThrow()
    {
        var c = new StubComponent(Span());
        c.StateHasChanged();
    }

    [Fact]
    public async Task StateHasChangedAsync_NoHandle_ReturnsCompletedTask()
    {
        var c = new StubComponent(Span());
        await c.StateHasChangedAsync();
    }

    [Fact]
    public void Component_GetsHandleFromSession_OnConstruction()
    {
        var session = NewSession(out _);
        Assert.Same(session, session.View.RenderHandle);
    }

    [Fact]
    public async Task RequestRenderAsync_NoSocketAttached_NoOps()
    {
        var session = NewSession(out _);
        await session.RequestRenderAsync();
        // Lock must remain free after the call.
        Assert.True(session.Lock.Wait(0));
        session.Lock.Release();
    }

    [Fact]
    public async Task RequestRenderAsync_FromInsideHandlerScope_DoesNotAcquireLock()
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
    public async Task RequestRenderAsync_OutsideHandlerScope_LeavesLockFree()
    {
        var session = NewSession(out _);
        await session.RequestRenderAsync();
        Assert.True(session.Lock.Wait(0));
        session.Lock.Release();
        Assert.False(session.InHandlerScope);
    }

    [Fact]
    public void StateHasChangedFromChild_PropagatesHandleViaLiveRenderContext()
    {
        var session = NewSession(out _);
        var child = new StubComponent(Span());

        using (LiveRenderContext.Begin(session.View, session.Services))
        {
            var resolved = LiveRenderContext.Current!.GetOrCreate(_ => child);
            Assert.Same(session, resolved.RenderHandle);
        }
    }

    private static LiveSession NewSession(out IServiceScope scope)
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return new LiveSession("test-session", new StubComponent(Span()), scope);
    }
}
