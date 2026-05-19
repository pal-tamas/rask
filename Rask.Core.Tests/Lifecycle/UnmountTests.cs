using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Server;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

[Collection("ConsoleRedirect")]
public class UnmountTests
{
    [Fact]
    public void OnUnmount_FiresOnce_OnDisposeComponentTree()
    {
        var c = new LifecycleTrackingComponent();
        c.RaiseLifecycleBeforeRender(propsChanged: true);

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.Equal(1, c.UnmountCount);
        Assert.Equal(1, c.UnmountAsyncCount);
    }

    [Fact]
    public async Task OnUnmount_FiresOnce_OnDisposeComponentTreeAsync()
    {
        var c = new LifecycleTrackingComponent();
        c.RaiseLifecycleBeforeRender(propsChanged: true);

        await ComponentLifecycle.DisposeComponentTreeAsync(c);

        Assert.Equal(1, c.UnmountCount);
        Assert.Equal(1, c.UnmountAsyncCount);
    }

    [Fact]
    public void OnUnmount_DoesNotFire_IfNeverMounted()
    {
        // A component created but never reaching RaiseLifecycleBeforeRender must not receive
        // an OnUnmount — symmetric with OnMount's _hasInitialized guard.
        var c = new LifecycleTrackingComponent();

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.Equal(0, c.UnmountCount);
        Assert.Equal(0, c.UnmountAsyncCount);
    }

    [Fact]
    public void OnUnmount_FiresBefore_CancellationTokenCancelled()
    {
        // The whole point of the hook: user code can still observe a live token at the
        // moment of unmount, so it can clean up resources that need the token (e.g. wait
        // for an in-flight task to acknowledge cancellation via a separate signal).
        var c = new TokenObservingUnmount();
        c.RaiseLifecycleBeforeRender(propsChanged: true);
        _ = c.GrabToken(); // Force CTS allocation so we can observe it.

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.True(c.UnmountFired);
        Assert.False(c.WasCancelledAtUnmount);
    }

    [Fact]
    public void OnUnmount_FiresBefore_UserDispose()
    {
        var order = new List<string>();
        var c = new UnmountThenDisposable(order);
        c.RaiseLifecycleBeforeRender(propsChanged: true);

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.Equal(new[] { "unmount", "dispose" }, order);
    }

    [Fact]
    public void OnUnmount_BottomUp_ChildrenBeforeParents()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var order = new List<string>();
        var leaf = new OrderingUnmount(order, "leaf");
        var middle = new OrderingMiddle(order, "middle", leaf);
        var root = new SwitchableHost(middle);
        var session = new LiveSession("test", root, scope);

        root.IncludeChild = true;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        session.Dispose();

        Assert.Equal(new[] { "leaf", "middle" }, order);
    }

    [Fact]
    public void OnUnmount_FiresOnTreeRemoval_ViaRenderAsLiveRoot()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var leaf = new LifecycleTrackingComponent();
        var root = new SwitchableHost(leaf);
        var session = new LiveSession("test", root, scope);

        root.IncludeChild = true;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        Assert.Equal(0, leaf.UnmountCount);

        root.IncludeChild = false;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        Assert.Equal(1, leaf.UnmountCount);
    }

    [Fact]
    public async Task OnUnmountAsync_Awaited_OnAsyncDisposePath()
    {
        var tcs = new TaskCompletionSource();
        var c = new LifecycleTrackingComponent { OnUnmountAsyncImpl = () => tcs.Task };
        c.RaiseLifecycleBeforeRender(propsChanged: true);

        var disposeTask = ComponentLifecycle.DisposeComponentTreeAsync(c);
        await Task.Delay(50);
        Assert.False(disposeTask.IsCompleted);

        tcs.SetResult();
        await disposeTask;
        Assert.True(disposeTask.IsCompleted);
    }

    [Fact]
    public async Task OnUnmountAsync_FireAndForgetWithFaultLogged_OnSyncDisposePath()
    {
        var tcs = new TaskCompletionSource();
        var c = new LifecycleTrackingComponent
        {
            OnUnmountAsyncImpl = async () =>
            {
                await tcs.Task;
                throw new InvalidOperationException("async-unmount-fault");
            }
        };
        c.RaiseLifecycleBeforeRender(propsChanged: true);

        var origErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            // Sync path returns immediately even though the async hook is still pending.
            ComponentLifecycle.DisposeComponentTree(c);

            // Now complete the fault and let the fire-and-forget observer log it.
            tcs.SetResult();
            for (var i = 0; i < 20 && !sw.ToString().Contains("async-unmount-fault"); i++)
            {
                await Task.Delay(10);
            }
        }
        finally
        {
            Console.SetError(origErr);
        }

        Assert.Contains("async-unmount-fault", sw.ToString());
        Assert.Contains("LifecycleTrackingComponent", sw.ToString());
    }

    [Fact]
    public void OnUnmount_Throws_LoggedAndSiblingsStillTornDown()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var throwing = new LifecycleTrackingComponent
        {
            OnUnmountImpl = () => throw new InvalidOperationException("boom")
        };
        var sibling = new LifecycleTrackingComponent();
        var root = new TwoChildHost(throwing, sibling) { IncludeChildren = true };
        var session = new LiveSession("test", root, scope);

        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        var origErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            root.IncludeChildren = false;
            session.View.RenderAsLiveRoot(scope.ServiceProvider);
        }
        finally
        {
            Console.SetError(origErr);
        }

        Assert.Equal(1, throwing.UnmountCount);
        Assert.Equal(1, sibling.UnmountCount);
        Assert.Contains("boom", sw.ToString());
    }

    [Fact]
    public void OnUnmount_DoesNotTriggerErrorBoundary()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var child = new LifecycleTrackingComponent
        {
            OnUnmountImpl = () => throw new InvalidOperationException("unmount-boom")
        };
        var boundary = new ErrorBoundary();
        boundary.SetProps(new Child[] { child }, fallback: null);

        using (LiveRenderContext.Begin(boundary, sp))
        {
            // Render walk stamps `child.Boundary = boundary`.
            _ = boundary.ToHtml();
            child.RaiseLifecycleBeforeRender(propsChanged: true);
        }

        var origErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            ComponentLifecycle.DisposeComponentTree(child);
        }
        finally
        {
            Console.SetError(origErr);
        }

        Assert.Null(boundary.Error);
        Assert.Contains("unmount-boom", sw.ToString());
    }

    [Fact]
    public void OnUnmount_FiresBefore_LegacyCancellationTokenRegister()
    {
        var order = new List<string>();
        var c = new HybridCleanup(order);
        c.RaiseLifecycleBeforeRender(propsChanged: true);

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.Equal(new[] { "unmount", "cancel-callback" }, order);
    }

    private sealed class TokenObservingUnmount : Component
    {
        public bool UnmountFired;
        public bool WasCancelledAtUnmount;

        public CancellationToken GrabToken() => CancellationToken;

        protected override void OnUnmount()
        {
            UnmountFired = true;
            WasCancelledAtUnmount = CancellationToken.IsCancellationRequested;
        }

        protected override Component Render() => Span();
    }

    private sealed class UnmountThenDisposable : Component, IDisposable
    {
        private readonly List<string> _order;
        public UnmountThenDisposable(List<string> order) => _order = order;
        public void Dispose() => _order.Add("dispose");
        protected override void OnUnmount() => _order.Add("unmount");
        protected override Component Render() => Span();
    }

    private sealed class OrderingUnmount : Component
    {
        private readonly List<string> _order;
        private readonly string _name;
        public OrderingUnmount(List<string> order, string name) { _order = order; _name = name; }
        protected override void OnUnmount() => _order.Add(_name);
        protected override Component Render() => Span();
    }

    private sealed class OrderingMiddle : Component
    {
        private readonly List<string> _order;
        private readonly string _name;
        private readonly Component _child;
        public OrderingMiddle(List<string> order, string name, Component child)
        {
            _order = order; _name = name; _child = child;
        }
        protected override void OnUnmount() => _order.Add(_name);
        protected override Component Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _child);
            ctx.NotifyParameters(c, true);
            return c;
        }
    }

    private sealed class SwitchableHost : Component
    {
        private readonly Component _child;
        public bool IncludeChild;
        public SwitchableHost(Component child) => _child = child;

        protected override Component Render()
        {
            if (!IncludeChild)
            {
                return Span();
            }

            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _child);
            ctx.NotifyParameters(c, true);
            return c;
        }
    }

    private sealed class TwoChildHost : Component
    {
        private readonly Component _a;
        private readonly Component _b;
        public bool IncludeChildren;
        public TwoChildHost(Component a, Component b) { _a = a; _b = b; }

        protected override Component Render()
        {
            if (!IncludeChildren)
            {
                return Span();
            }

            var ctx = LiveRenderContext.Current!;
            var ca = ctx.GetOrCreate(_ => _a);
            ctx.NotifyParameters(ca, true);
            var cb = ctx.GetOrCreate(_ => _b);
            ctx.NotifyParameters(cb, true);
            return Div()[ca, cb];
        }
    }

    private sealed class HybridCleanup : Component
    {
        private readonly List<string> _order;
        public HybridCleanup(List<string> order) => _order = order;

        protected override void OnMount() =>
            CancellationToken.Register(() => _order.Add("cancel-callback"));

        protected override void OnUnmount() => _order.Add("unmount");

        protected override Component Render() => Span();
    }
}
