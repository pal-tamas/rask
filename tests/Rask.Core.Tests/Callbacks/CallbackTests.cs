using Rask.Core;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Callbacks;

public class CallbackTests
{
    [Fact]
    public async Task TypedCallback_Invoke_RerendersReceiverThroughCachedIntermediate()
    {
        // The core promise: a child invoking a parent-supplied callback re-renders the *receiver*
        // (the component that owns the delegate) even when that receiver is a render-cached
        // intermediate that nothing else dirtied.
        var sp = RenderHarness.EmptyServices();
        var host = new Host(Receiver.Mode.Sync);

        host.RenderAsLiveRoot(sp);
        host.RenderAsLiveRoot(sp);
        Assert.Equal(1, host.Receiver.RenderCount); // cached after first paint — stable props
        Assert.Contains("count=0", host.ToHtml());

        await host.Receiver.Child.Fire(5);

        host.RenderAsLiveRoot(sp);
        Assert.Equal(2, host.Receiver.RenderCount); // Callback re-rendered the receiver
        Assert.Contains("count=5", host.ToHtml());
    }

    [Fact]
    public async Task StaticHelper_OnPlainDelegateProp_RerendersOwner()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new Host(Receiver.Mode.Delegate);

        host.RenderAsLiveRoot(sp);
        host.RenderAsLiveRoot(sp);
        Assert.Equal(1, host.Receiver.RenderCount);

        await host.Receiver.Child.FireDelegate(7);

        host.RenderAsLiveRoot(sp);
        Assert.Equal(2, host.Receiver.RenderCount);
        Assert.Contains("count=7", host.ToHtml());
    }

    [Fact]
    public async Task AsyncCallback_Awaited_ThenRerenders()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new Host(Receiver.Mode.Async);

        host.RenderAsLiveRoot(sp);
        await host.Receiver.Child.Fire(3);

        host.RenderAsLiveRoot(sp);
        Assert.Contains("count=3", host.ToHtml());
    }

    [Fact]
    public async Task ImplicitConversion_FromMethodGroup_CapturesReceiver()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new Host(Receiver.Mode.ImplicitConv);

        host.RenderAsLiveRoot(sp);
        host.RenderAsLiveRoot(sp);
        Assert.Equal(1, host.Receiver.RenderCount);

        await host.Receiver.Child.Fire(9);

        host.RenderAsLiveRoot(sp);
        Assert.Equal(2, host.Receiver.RenderCount);
        Assert.Contains("count=9", host.ToHtml());
    }

    [Fact]
    public async Task EmptyCallback_Invoke_IsNoOp()
    {
        var cb = Callback<int>.Empty;
        Assert.False(cb.HasDelegate);
        await cb.InvokeAsync(5); // must not throw

        Assert.False(Callback.Empty.HasDelegate);
        await Callback.Empty.InvokeAsync();
    }

    [Fact]
    public async Task NullDelegate_Helper_IsNoOp()
    {
        await Callback.InvokeAsync((Action?)null);
        await Callback.InvokeAsync((Func<Task>?)null);
        await Callback.InvokeAsync((Action<int>?)null, 1);
        await Callback.InvokeAsync((Func<int, Task>?)null, 1);
    }

    [Fact]
    public async Task Arg_PassesThrough_ToHandler()
    {
        var holder = new ArgHolder();
        await holder.Fire(42);
        Assert.Equal(42, holder.Seen);
    }

    // ---- helpers ----

    // Root that holds a render-cached intermediate (Receiver).
    private sealed class Host : Component
    {
        public readonly Receiver Receiver;

        public Host(Receiver.Mode mode) => Receiver = new Receiver(mode);

        protected override RenderResult Render()
        {
            var ctx = LiveRenderContext.Current!;
            var r = ctx.GetOrCreate(_ => Receiver);
            ctx.NotifyParameters(r, false); // stable props ⇒ Receiver caches after first render
            return Div()[r];
        }
    }

    // The callbacks are created *inside* Receiver so each lambda captures `this` (compiled as an
    // instance method ⇒ delegate.Target is the component) — the shape real components use.
    private sealed class Receiver : Component
    {
        public enum Mode { Sync, Async, Delegate, ImplicitConv }

        private readonly Mode _mode;
        public readonly Fireable Child = new();
        public int Count;
        public int RenderCount;
        public Callback<int> OnAdd;
        public Action<int>? OnAddDelegate;

        public Receiver(Mode mode) => _mode = mode;

        protected override RenderResult Render()
        {
            RenderCount++;
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => Child);
            ctx.NotifyParameters(c, false);

            switch (_mode)
            {
                case Mode.Sync:
                    OnAdd = Callback.Create<int>(n => Count += n);
                    break;
                case Mode.Async:
                    OnAdd = Callback.Create<int>(async n =>
                    {
                        await Task.Yield();
                        Count += n;
                    });
                    break;
                case Mode.Delegate:
                    OnAddDelegate = n => Count += n;
                    break;
                case Mode.ImplicitConv:
                    Action<int> add = Add; // method group, Target == this
                    OnAdd = add;           // implicit Action<int> -> Callback<int>
                    break;
            }

            c.Owner = this;
            return Span()[$"count={Count}"];
        }

        private void Add(int n) => Count += n;
    }

    private sealed class Fireable : Component
    {
        public Receiver? Owner;

        protected override RenderResult Render() => Span()["x"];

        public ValueTask Fire(int n) => Owner!.OnAdd.InvokeAsync(n);

        public ValueTask FireDelegate(int n) => Callback.InvokeAsync(Owner!.OnAddDelegate, n);
    }

    private sealed class ArgHolder : Component
    {
        public int Seen = -1;

        protected override RenderResult Render() => new Fragment();

        public ValueTask Fire(int n)
        {
            var cb = Callback.Create<int>(v => Seen = v);
            return cb.InvokeAsync(n);
        }
    }
}
