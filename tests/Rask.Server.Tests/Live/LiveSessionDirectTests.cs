using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;

namespace Rask.Server.Tests.Live;

public class LiveSessionDirectTests
{
    [Fact]
    public async Task RequestRenderAsync_NoSocket_NoOps()
    {
        using var session = NewSession(new BasicComponent());

        await session.RequestRenderAsync();
    }

    [Fact]
    public void Dispose_DisposesScopeAndDisposesComponentTree()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var disposable = new TrackingDisposable();
        var session = new LiveSession("id", disposable, scope);

        session.Dispose();

        Assert.Equal(1, disposable.Disposes);
    }

    [Fact]
    public async Task DisposeAsync_RunsAsyncDispose()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var disposable = new TrackingAsyncDisposable();
        var session = new LiveSession("id", disposable, scope);

        await session.DisposeAsync();

        Assert.Equal(1, disposable.Disposes);
    }

    [Fact]
    public async Task Constructor_AssignsRenderHandleSoStateHasChangedAsyncRoutesToSession()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var view = new BasicComponent();

        using var session = new LiveSession("id", view, scope);

        await view.StateHasChangedAsync();
    }

    private static LiveSession NewSession(Component view)
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return new LiveSession(Guid.NewGuid().ToString("N"), view, scope);
    }

    private sealed class BasicComponent : Component
    {
        protected override Component? Render() => new Span();
    }

    private sealed class TrackingDisposable : Component, IDisposable
    {
        public int Disposes;
        public void Dispose() => Disposes++;
        protected override Component? Render() => new Span();
    }

    private sealed class TrackingAsyncDisposable : Component, IAsyncDisposable
    {
        public int Disposes;

        public ValueTask DisposeAsync()
        {
            Disposes++;
            return ValueTask.CompletedTask;
        }

        protected override Component? Render() => new Span();
    }
}
