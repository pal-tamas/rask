using System.Reflection;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public class ComponentCancellationTests
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
        var field = typeof(Component)
            .GetField("_lifetimeCts", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        Assert.Null(field!.GetValue(c));

        ComponentLifecycle.DisposeComponentTree(c);

        Assert.Null(field.GetValue(c));
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

    internal sealed class CancellationProbe : Component
    {
        public Func<CancellationToken, Task>? OnMountAsyncImpl;

        public CancellationToken Token => CancellationToken;

        protected override Task OnMountAsync() =>
            OnMountAsyncImpl?.Invoke(CancellationToken) ?? Task.CompletedTask;

        protected override RenderResult Render() => Span();
    }

    private sealed class Root : Component
    {
        protected override RenderResult Render() => Span();
    }

    private sealed class TokenWatchingDisposable : Component, IDisposable
    {
        public bool SawCancellation;

        public CancellationToken Token => CancellationToken;

        public void Dispose() => SawCancellation = CancellationToken.IsCancellationRequested;

        protected override RenderResult Render() => Span();
    }
}
