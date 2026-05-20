using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Server;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

[Collection("ConsoleRedirect")]
public class DisposeErrorTests
{
    [Fact]
    public void DisposeComponentTree_ChildThrows_LogsAndContinues()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var faulty = new FaultingDisposable();
        var ok = new RecordingDisposable();
        var root = new TwoChildHost(faulty, ok) { Include = true };
        var session = new LiveSession("test", root, scope);
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        using var stderr = new StringWriter();
        var prev = Console.Error;
        Console.SetError(stderr);
        try { session.Dispose(); }
        finally { Console.SetError(prev); }

        Assert.Equal(1, faulty.Attempts);
        Assert.Equal(1, ok.Disposes);
        Assert.Contains("FaultingDisposable", stderr.ToString());
    }

    [Fact]
    public async Task DisposeComponentTreeAsync_AsyncDisposeFaults_LogsAndContinues()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var faulty = new FaultingAsyncDisposable();
        var ok = new RecordingDisposable();
        var root = new TwoChildHost(faulty, ok) { Include = true };
        var session = new LiveSession("test", root, scope);
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        using var stderr = new StringWriter();
        var prev = Console.Error;
        Console.SetError(stderr);
        try { await session.DisposeAsync(); }
        finally { Console.SetError(prev); }

        Assert.Equal(1, faulty.Attempts);
        Assert.Equal(1, ok.Disposes);
        Assert.Contains("FaultingAsyncDisposable", stderr.ToString());
    }

    private sealed class FaultingDisposable : Component, IDisposable
    {
        public int Attempts;

        public void Dispose()
        {
            Attempts++;
            throw new InvalidOperationException("boom");
        }

        protected override Component Render() => Span();
    }

    private sealed class FaultingAsyncDisposable : Component, IAsyncDisposable
    {
        public int Attempts;

        public ValueTask DisposeAsync()
        {
            Attempts++;
            throw new InvalidOperationException("boom-async");
        }

        protected override Component Render() => Span();
    }

    private sealed class RecordingDisposable : Component, IDisposable
    {
        public int Disposes;
        public void Dispose() => Disposes++;
        protected override Component Render() => Span();
    }

    private sealed class TwoChildHost : Component
    {
        private readonly Component _a;
        private readonly Component _b;
        public bool Include;

        public TwoChildHost(Component a, Component b)
        {
            _a = a;
            _b = b;
        }

        protected override Component Render()
        {
            if (!Include)
            {
                return Span();
            }

            var ctx = LiveRenderContext.Current!;
            var first = ctx.GetOrCreate(_a.GetType(), _ => _a);
            ctx.NotifyParameters(first, true);
            var second = ctx.GetOrCreate(_b.GetType(), _ => _b);
            ctx.NotifyParameters(second, true);
            return Div()[first, second];
        }
    }
}
