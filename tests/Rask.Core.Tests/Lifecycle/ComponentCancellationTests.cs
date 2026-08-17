using System.Reflection;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public partial class ComponentCancellationTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void CancellationToken_BeforeDispose_NotCancelled()
    {
        var sp = RenderHarness.EmptyServices();
        var root = new Root();
        var c = new CancellationProbe();

        using (var ctx = LiveRenderContext.Begin(root, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        Assert.False(c.Token.IsCancellationRequested);
    }

    [Fact]
    public void DisposeComponentTree_CancelsLifetimeToken()
    {
        var c = new CancellationProbe();
        var token = c.Token;

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task DisposeComponentTreeAsync_CancelsLifetimeToken()
    {
        var c = new CancellationProbe();
        var token = c.Token;

        await ComponentLifecycle.DisposeComponentTreeAsync(c);

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task InFlightOnMountAsync_ObservesCancellation_OnDispose()
    {
        var sp = RenderHarness.EmptyServices();
        var observed = new TaskCompletionSource();
        CancellationToken capturedToken = default;

        var root = new Root();
        var c = new CancellationProbe
        {
            OnMountAsyncImpl = async ct =>
            {
                capturedToken = ct;
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    observed.TrySetResult();
                    throw;
                }
            }
        };

        using (var ctx = LiveRenderContext.Begin(root, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        Assert.False(capturedToken.IsCancellationRequested);

        ComponentLifecycle.DisposeComponentTree(root);

        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(capturedToken.IsCancellationRequested);
    }

    [Fact]
    public void CancellationToken_NeverAccessed_NoCtsAllocated()
    {
        var c = new CancellationProbe();
        // The lifetime CTS is hoisted into the lazy LiveState container. A component that never
        // touches its token (nor any other live-render path) allocates no LiveState at all up front —
        // a stronger guarantee than the pre-hoist "field stays null": there is no container to hold it.
        var liveField = typeof(Component)
            .GetField("_live", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(liveField);
        Assert.Null(liveField!.GetValue(c));

        ComponentLifecycle.DisposeComponentTree(c);

        // Dispose stamps lifecycle flags, so a LiveState may now exist — but it must never have
        // allocated a CancellationTokenSource for a token that was never observed.
        var live = liveField.GetValue(c);
        if (live is not null)
        {
            var cts = live.GetType()
                .GetField("LifetimeCts", BindingFlags.Instance | BindingFlags.Public)!
                .GetValue(live);
            Assert.Null(cts);
        }
    }

    [Fact]
    public void Dispose_CallsUserDisposeAfterCancellation()
    {
        var c = new TokenWatchingDisposable();
        var token = c.Token;

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.True(token.IsCancellationRequested);
        Assert.True(c.SawCancellation);
    }

    internal sealed partial class CancellationProbe : Component
    {
        public Func<CancellationToken, Task>? OnMountAsyncImpl;

        public CancellationToken Token => CancellationToken;

        protected override Task OnMountAsync() =>
            OnMountAsyncImpl?.Invoke(CancellationToken) ?? Task.CompletedTask;

        protected override Component? Render() => Span;
    }

    private sealed class Root : Component
    {
        protected override Component? Render() => Span;
    }

    private sealed class TokenWatchingDisposable : Component, IDisposable
    {
        public bool SawCancellation;

        public CancellationToken Token => CancellationToken;

        public void Dispose() => SawCancellation = CancellationToken.IsCancellationRequested;

        protected override Component? Render() => Span;
    }
}
