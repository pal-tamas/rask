using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Callbacks;

public class AutoCallbackTests
{
    [Fact]
    public async Task Wrap_SyncCallback_RerendersReceiverThroughCachedIntermediate()
    {
        // The core promise: a child invoking a parent-supplied (auto-wrapped) delegate re-renders
        // the *receiver* — the component that owns the delegate — even when that receiver is a
        // render-cached intermediate that nothing else dirtied.
        var sp = RenderHarness.EmptyServices();
        var host = new Host(Receiver.Mode.Sync);

        host.RenderAsLiveRoot(sp);
        host.RenderAsLiveRoot(sp);
        Assert.Equal(1, host.Receiver.RenderCount); // cached after first paint — stable props
        Assert.Contains("count=0", host.ToHtml());

        await host.Receiver.Child.Fire(5);

        host.RenderAsLiveRoot(sp);
        Assert.Equal(2, host.Receiver.RenderCount); // the wrapper re-rendered the receiver
        Assert.Contains("count=5", host.ToHtml());
    }

    [Fact]
    public async Task Wrap_ChildWrapsInOwnLambda_StillRerendersParent()
    {
        // The case plain handler-owner resolution misses: the child invokes the parent delegate
        // from inside *its own* lambda (Target == child), off the direct DOM path. The wrapper
        // still re-renders the parent.
        var sp = RenderHarness.EmptyServices();
        var host = new Host(Receiver.Mode.Sync);

        host.RenderAsLiveRoot(sp);
        host.RenderAsLiveRoot(sp);
        Assert.Equal(1, host.Receiver.RenderCount);

        await host.Receiver.Child.FireWrappedInOwnLambda(8);

        host.RenderAsLiveRoot(sp);
        Assert.Equal(2, host.Receiver.RenderCount);
        Assert.Contains("count=8", host.ToHtml());
    }

    [Fact]
    public async Task Wrap_AsyncCallback_AwaitsThenRerenders()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new Host(Receiver.Mode.Async);

        host.RenderAsLiveRoot(sp);
        await host.Receiver.Child.FireAsync(3); // mutation happens only after an awaited Task.Yield

        host.RenderAsLiveRoot(sp);
        Assert.Contains("count=3", host.ToHtml());
    }

    [Fact]
    public void Wrap_NullDelegate_ReturnsNull()
    {
        Assert.Null(AutoCallback.Wrap((Action?)null));
        Assert.Null(AutoCallback.Wrap(null));
        Assert.Null(AutoCallback.Wrap((Action<int>?)null));
        Assert.Null(AutoCallback.Wrap((Func<int, Task>?)null));
    }

    [Fact]
    public void Wrap_NonComponentTarget_ReturnsOriginalUnchanged()
    {
        // A static method (Target == null) and a lambda closing over a local (Target == a compiler
        // closure, not a Component) have no component to re-render — Wrap returns them unchanged,
        // so there is no extra allocation and no spurious re-render (same limitation Blazor's
        // EventCallback and the old Callback had).
        var staticMethod = NoOp;
        Assert.Same(staticMethod, AutoCallback.Wrap(staticMethod));

        var local = 0;
        Action<int> closesOverLocal = n => local = n;
        Assert.Same(closesOverLocal, AutoCallback.Wrap(closesOverLocal));
        _ = local;
    }

    [Fact]
    public async Task Wrap_PassesArgThrough()
    {
        var holder = new ArgHolder();
        await holder.Fire(42);
        Assert.Equal(42, holder.Seen);
    }

    [Fact]
    public async Task Wrap_GenericReferenceArg_PassesThroughAndRerenders()
    {
        // Exercises a non-int T (the same generic Wrap<T> overload covers DOM-handler shapes like
        // Action<string>).
        var sp = RenderHarness.EmptyServices();
        var host = new Host(Receiver.Mode.StringArg);

        host.RenderAsLiveRoot(sp);
        await host.Receiver.Child.FireString("hello");

        host.RenderAsLiveRoot(sp);
        Assert.Contains("text=hello", host.ToHtml());
    }

    private static void NoOp(int n)
    {
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
    // instance method ⇒ delegate.Target is the component), then wrapped via AutoCallback exactly as
    // the generated factory would — the shape real components use.
    private sealed class Receiver : Component
    {
        public enum Mode { Sync, Async, StringArg }

        private readonly Mode _mode;
        public readonly Fireable Child = new();
        public int Count;
        public Action<int>? OnAdd;
        public Func<int, Task>? OnAddAsync;
        public Action<string>? OnText;
        public int RenderCount;
        public string? Text;

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
                    OnAdd = AutoCallback.Wrap<int>(n => Count += n);
                    break;
                case Mode.Async:
                    OnAddAsync = AutoCallback.Wrap<int>(async n =>
                    {
                        await Task.Yield();
                        Count += n;
                    });
                    break;
                case Mode.StringArg:
                    OnText = AutoCallback.Wrap<string>(s => Text = s);
                    break;
            }

            c.Owner = this;
            return Span()[_mode == Mode.StringArg ? $"text={Text}" : $"count={Count}"];
        }
    }

    private sealed class Fireable : Component
    {
        public Receiver? Owner;

        protected override RenderResult Render() => Span()["x"];

        public ValueTask Fire(int n)
        {
            Owner!.OnAdd!(n);
            return default;
        }

        // The child interposes its own lambda before invoking — Target is this child, so the DOM
        // path would only dirty the child; the wrapped parent delegate re-renders the parent.
        public ValueTask FireWrappedInOwnLambda(int n)
        {
            var wrappedInChildLambda = () => Owner!.OnAdd!(n);
            wrappedInChildLambda();
            return default;
        }

        public Task FireAsync(int n) => Owner!.OnAddAsync!(n);

        public ValueTask FireString(string s)
        {
            Owner!.OnText!(s);
            return default;
        }
    }

    private sealed class ArgHolder : Component
    {
        public int Seen = -1;

        protected override RenderResult Render() => new Fragment();

        public ValueTask Fire(int n)
        {
            var cb = AutoCallback.Wrap<int>(v => Seen = v);
            cb!(n);
            return default;
        }
    }
}
