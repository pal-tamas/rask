using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Server;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

[Collection("ConsoleRedirect")]
public partial class UnmountTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void OnUnmount_fires_once_when_the_tree_is_disposed()
    {
        var c = new LifecycleTrackingComponent();
        c.RaiseLifecycleBeforeRender(true);

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.Equal(1, c.UnmountCount);
        Assert.Equal(1, c.UnmountAsyncCount);
    }

    [Fact]
    public async Task OnUnmount_fires_once_when_the_tree_is_disposed_asynchronously()
    {
        var c = new LifecycleTrackingComponent();
        c.RaiseLifecycleBeforeRender(true);

        await ComponentLifecycle.DisposeComponentTreeAsync(c);

        Assert.Equal(1, c.UnmountCount);
        Assert.Equal(1, c.UnmountAsyncCount);
    }

    [Fact]
    public void Disposing_the_tree_twice_tears_down_only_once()
    {
        // A tree mutation inside a Unmount hook could route the same node through a second
        // dispose pass. The one-shot guard (Component.TryBeginDispose) must keep Unmount and
        // the user's Dispose firing exactly once even when DisposeComponentTree is re-entered.
        var disposeCount = 0;
        var c = new CountingDisposable(() => disposeCount++);
        c.RaiseLifecycleBeforeRender(true);

        ComponentLifecycle.DisposeComponentTree(c);
        ComponentLifecycle.DisposeComponentTree(c);

        Assert.Equal(1, c.UnmountCount);
        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public async Task Disposing_the_tree_asynchronously_twice_tears_down_only_once()
    {
        var disposeCount = 0;
        var c = new CountingDisposable(() => disposeCount++);
        c.RaiseLifecycleBeforeRender(true);

        await ComponentLifecycle.DisposeComponentTreeAsync(c);
        await ComponentLifecycle.DisposeComponentTreeAsync(c);

        Assert.Equal(1, c.UnmountCount);
        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public void A_child_cleared_during_the_parents_unmount_is_not_disposed_twice()
    {
        // The parent's Unmount mutates its own persisted children (a realistic teardown
        // pattern). The child was already disposed bottom-up before the parent's hook ran, so
        // re-touching it must not re-run its Unmount / Dispose — the guard absorbs it.
        var sp = RenderHarness.EmptyServices();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var childDisposeCount = 0;
        var child = new CountingDisposable(() => childDisposeCount++);
        var host = new ClearChildrenOnUnmountHost(child);
        var session = new LiveSession("test", host, scope, LiveDiffMode.Auto);

        host.IncludeChild = true;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        session.Dispose();

        Assert.Equal(1, child.UnmountCount);
        Assert.Equal(1, childDisposeCount);
    }

    [Fact]
    public void OnUnmount_does_not_fire_if_never_mounted()
    {
        // A component created but never reaching RaiseLifecycleBeforeRender must not receive
        // a Unmount — symmetric with Mount's _hasInitialized guard.
        var c = new LifecycleTrackingComponent();

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.Equal(0, c.UnmountCount);
        Assert.Equal(0, c.UnmountAsyncCount);
    }

    [Fact]
    public void OnUnmount_fires_before_the_cancellation_token_is_cancelled()
    {
        // The whole point of the hook: user code can still observe a live token at the
        // moment of unmount, so it can clean up resources that need the token (e.g. wait
        // for an in-flight task to acknowledge cancellation via a separate signal).
        var c = new TokenObservingUnmount();
        c.RaiseLifecycleBeforeRender(true);
        _ = c.GrabToken(); // Force CTS allocation so we can observe it.

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.True(c.UnmountFired);
        Assert.False(c.WasCancelledAtUnmount);
    }

    [Fact]
    public void OnUnmount_fires_before_the_users_dispose()
    {
        var order = new List<string>();
        var c = new UnmountThenDisposable(order);
        c.RaiseLifecycleBeforeRender(true);

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.Equal(new[] { "unmount", "dispose" }, order);
    }

    [Fact]
    public void OnUnmount_runs_bottom_up_with_children_before_parents()
    {
        var sp = RenderHarness.EmptyServices();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var order = new List<string>();
        var leaf = new OrderingUnmount(order, "leaf");
        var middle = new OrderingMiddle(order, "middle", leaf);
        var root = new SwitchableHost(middle);
        var session = new LiveSession("test", root, scope, LiveDiffMode.Auto);

        root.IncludeChild = true;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        session.Dispose();

        Assert.Equal(new[] { "leaf", "middle" }, order);
    }

    [Fact]
    public void OnUnmount_fires_on_removal_from_the_tree_via_a_live_root_render()
    {
        var sp = RenderHarness.EmptyServices();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var leaf = new LifecycleTrackingComponent();
        var root = new SwitchableHost(leaf);
        var session = new LiveSession("test", root, scope, LiveDiffMode.Auto);

        root.IncludeChild = true;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        Assert.Equal(0, leaf.UnmountCount);

        root.IncludeChild = false;
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        Assert.Equal(1, leaf.UnmountCount);
    }

    [Fact]
    public async Task OnUnmountAsync_is_awaited_on_the_async_dispose_path()
    {
        var tcs = new TaskCompletionSource();
        var c = new LifecycleTrackingComponent { OnUnmountAsyncImpl = () => tcs.Task };
        c.RaiseLifecycleBeforeRender(true);

        var disposeTask = ComponentLifecycle.DisposeComponentTreeAsync(c);
        await Task.Delay(50);
        Assert.False(disposeTask.IsCompleted);

        tcs.SetResult();
        await disposeTask;
        Assert.True(disposeTask.IsCompleted);
    }

    [Fact]
    public async Task OnUnmountAsync_is_fire_and_forget_with_its_fault_logged_on_the_sync_dispose_path()
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
        c.RaiseLifecycleBeforeRender(true);

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
    public void A_throwing_OnUnmount_is_logged_and_the_siblings_are_still_torn_down()
    {
        var sp = RenderHarness.EmptyServices();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var throwing = new LifecycleTrackingComponent
        {
            OnUnmountImpl = () => throw new InvalidOperationException("boom")
        };
        var sibling = new LifecycleTrackingComponent();
        var root = new TwoChildHost(throwing, sibling) { IncludeChildren = true };
        var session = new LiveSession("test", root, scope, LiveDiffMode.Auto);

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
    public void OnUnmount_does_not_trigger_an_error_boundary()
    {
        var sp = RenderHarness.EmptyServices();
        var child = new LifecycleTrackingComponent
        {
            OnUnmountImpl = () => throw new InvalidOperationException("unmount-boom")
        };
        var boundary = new ErrorBoundary();
        boundary.SetProps(new Component[] { child }, null);

        using (LiveRenderContext.Begin(boundary, sp))
        {
            // Render walk stamps `child.Boundary = boundary`.
            _ = boundary.ToHtml();
            child.RaiseLifecycleBeforeRender(true);
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
    public void OnUnmount_fires_before_a_legacy_cancellation_token_registration()
    {
        var order = new List<string>();
        var c = new HybridCleanup(order);
        c.RaiseLifecycleBeforeRender(true);

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.Equal(new[] { "unmount", "cancel-callback" }, order);
    }

    private sealed class CountingDisposable : Component, IDisposable
    {
        private readonly Action _onDispose;
        public int UnmountCount;
        public CountingDisposable(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
        protected override Task OnUnmount()
        {
            UnmountCount++;
            return Task.CompletedTask;
        }
        protected override Component? Render() => Span;
    }

    // Re-disposes its already-disposed child from its own Unmount, mimicking a teardown that
    // mutates the tree mid-unmount. The one-shot guard must absorb the second pass.
    private sealed class ClearChildrenOnUnmountHost : Component
    {
        private readonly Component _child;
        public bool IncludeChild;
        public ClearChildrenOnUnmountHost(Component child) => _child = child;

        protected override Task OnUnmount()
        {
            ComponentLifecycle.DisposeComponentTree(_child);
            return Task.CompletedTask;
        }

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

    private sealed class TokenObservingUnmount : Component
    {
        public bool UnmountFired;
        public bool WasCancelledAtUnmount;

        public CancellationToken GrabToken() => CancellationToken;

        protected override Task OnUnmount()
        {
            UnmountFired = true;
            WasCancelledAtUnmount = CancellationToken.IsCancellationRequested;
            return Task.CompletedTask;
        }

        protected override Component? Render() => Span;
    }

    private sealed class UnmountThenDisposable : Component, IDisposable
    {
        private readonly List<string> _order;
        public UnmountThenDisposable(List<string> order) => _order = order;
        public void Dispose() => _order.Add("dispose");
        protected override Task OnUnmount()
        {
            _order.Add("unmount");
            return Task.CompletedTask;
        }
        protected override Component? Render() => Span;
    }

    private sealed class OrderingUnmount : Component
    {
        private readonly string _name;
        private readonly List<string> _order;

        public OrderingUnmount(List<string> order, string name)
        {
            _order = order;
            _name = name;
        }

        protected override Task OnUnmount()
        {
            _order.Add(_name);
            return Task.CompletedTask;
        }
        protected override Component? Render() => Span;
    }

    private sealed class OrderingMiddle : Component
    {
        private readonly Component _child;
        private readonly string _name;
        private readonly List<string> _order;

        public OrderingMiddle(List<string> order, string name, Component child)
        {
            _order = order;
            _name = name;
            _child = child;
        }

        protected override Task OnUnmount()
        {
            _order.Add(_name);
            return Task.CompletedTask;
        }

        protected override Component? Render()
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

    private sealed class TwoChildHost : Component
    {
        private readonly Component _a;
        private readonly Component _b;
        public bool IncludeChildren;

        public TwoChildHost(Component a, Component b)
        {
            _a = a;
            _b = b;
        }

        protected override Component? Render()
        {
            if (!IncludeChildren)
            {
                return Span;
            }

            var ctx = LiveRenderContext.Current!;
            var ca = ctx.GetOrCreate(_ => _a);
            ctx.NotifyParameters(ca, true);
            var cb = ctx.GetOrCreate(_ => _b);
            ctx.NotifyParameters(cb, true);
            return Div[ca, cb];
        }
    }

    private sealed class HybridCleanup : Component
    {
        private readonly List<string> _order;
        public HybridCleanup(List<string> order) => _order = order;

        protected override Task OnMount()
        {
            CancellationToken.Register(() => _order.Add("cancel-callback"));
            return Task.CompletedTask;
        }

        protected override Task OnUnmount()
        {
            _order.Add("unmount");
            return Task.CompletedTask;
        }

        protected override Component? Render() => Span;
    }
}
