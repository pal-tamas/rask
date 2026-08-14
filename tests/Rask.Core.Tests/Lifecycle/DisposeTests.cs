using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Server;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public class DisposeTests
{
    [Fact]
    public void RemovedFromTree_TriggersDispose()
    {
        var sp = RenderHarness.EmptyServices();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var disposable = new DisposableLeaf();
        var root = new SwitchableHost(disposable);
        var session = new LiveSession("test", root, scope, LiveDiffMode.Auto);

        root.IncludeChild = true;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        Assert.Equal(0, disposable.DisposeCount);

        root.IncludeChild = false;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        Assert.Equal(1, disposable.DisposeCount);
    }

    [Fact]
    public void SessionDispose_DisposesAllComponents()
    {
        var sp = RenderHarness.EmptyServices();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var disposable = new DisposableLeaf();
        var root = new SwitchableHost(disposable) { IncludeChild = true };
        var session = new LiveSession("test", root, scope, LiveDiffMode.Auto);

        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        Assert.Equal(0, disposable.DisposeCount);

        session.Dispose();

        Assert.Equal(1, disposable.DisposeCount);
    }

    [Fact]
    public async Task SessionDisposeAsync_AwaitsAsyncDisposable()
    {
        var sp = RenderHarness.EmptyServices();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var disposable = new AsyncDisposableLeaf();
        var root = new AsyncSwitchableHost(disposable);
        var session = new LiveSession("test", root, scope, LiveDiffMode.Auto);

        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        Assert.Equal(0, disposable.DisposeCount);

        await session.DisposeAsync();

        Assert.Equal(1, disposable.DisposeCount);
    }

    [Fact]
    public void RemovedFromTree_DisposesGrandchildrenRecursively()
    {
        var sp = RenderHarness.EmptyServices();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var grandchild = new DisposableLeaf();
        var middle = new DisposableMiddle(grandchild);
        var root = new SwitchableHost(middle);
        var session = new LiveSession("test", root, scope, LiveDiffMode.Auto);

        root.IncludeChild = true;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        root.IncludeChild = false;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        Assert.Equal(1, grandchild.DisposeCount);
        Assert.Equal(1, middle.DisposeCount);
    }

    private sealed class DisposableLeaf : Component, IDisposable
    {
        public int DisposeCount;
        public void Dispose() => DisposeCount++;
        protected override Component? Render() => Span;
    }

    private sealed class AsyncDisposableLeaf : Component, IAsyncDisposable
    {
        public int DisposeCount;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        protected override Component? Render() => Span;
    }

    private sealed class DisposableMiddle : Component, IDisposable
    {
        private readonly Component _grandchild;
        public int DisposeCount;
        public DisposableMiddle(Component grandchild) => _grandchild = grandchild;

        public void Dispose() => DisposeCount++;

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _grandchild);
            ctx.NotifyParameters(c, true);
            return c;
        }
    }

    private sealed class SwitchableHost : Component
    {
        private readonly Component _child;
        public bool IncludeChild;
        public SwitchableHost(Component child) => _child = child;

        protected override Component? Render()
        {
            if (!IncludeChild)
            {
                return Span;
            }

            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _child);
            ctx.NotifyParameters(c, true);
            return c;
        }
    }

    private sealed class AsyncSwitchableHost : Component
    {
        private readonly Component _child;
        public AsyncSwitchableHost(Component child) => _child = child;

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _child);
            ctx.NotifyParameters(c, true);
            return c;
        }
    }
}
