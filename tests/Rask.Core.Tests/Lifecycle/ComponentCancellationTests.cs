using System.Reflection;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public partial class ComponentCancellationTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_cancellation_token_is_not_cancelled_before_dispose()
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
    public void Disposing_the_tree_cancels_the_lifetime_token()
    {
        var c = new CancellationProbe();
        var token = c.Token;

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task Disposing_the_tree_asynchronously_cancels_the_lifetime_token()
    {
        var c = new CancellationProbe();
        var token = c.Token;

        await ComponentLifecycle.DisposeComponentTreeAsync(c);

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task An_in_flight_OnMountAsync_observes_cancellation_on_dispose()
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
    public async Task OnMountAsync_that_passes_no_token_is_still_cancelled_with_its_component()
    {
        // `await Cache.Remember(…)` in Mount passes no token; the ambient one has to be the
        // component's, before the first await and after it.
        var sp = RenderHarness.EmptyServices();
        var afterAwait = new TaskCompletionSource<CancellationToken>();
        CancellationToken beforeAwait = default;

        var root = new Root();
        var c = new CancellationProbe
        {
            OnMountAsyncImpl = async _ =>
            {
                beforeAwait = Ambient.CancellationToken;
                await Task.Yield();
                afterAwait.TrySetResult(Ambient.CancellationToken);
            }
        };

        using (var ctx = LiveRenderContext.Begin(root, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        var after = await afterAwait.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(c.Token, beforeAwait);
        Assert.Equal(c.Token, after);
        Assert.False(Ambient.CancellationToken.CanBeCanceled);   // nothing outside the hook

        ComponentLifecycle.DisposeComponentTree(root);
        Assert.True(after.IsCancellationRequested);
    }

    [Fact]
    public void A_cancellation_token_never_accessed_allocates_no_source()
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
    public void Disposing_calls_the_users_dispose_after_cancellation()
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

        protected override Task OnMount() =>
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
