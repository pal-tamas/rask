using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.Live;

/// <summary>
///     <see cref="LiveSessionStore.RerenderAllAsync" /> is the dev-time repaint the debounced
///     asset-change subscriber drives.
///     <para>
///         It used to call only <c>View.StateHasChangedAsync()</c>, which dirties the root and
///         nothing else — so any component whose subtree was cached replayed its previous frame and
///         an edit inside it never appeared. That made this path strictly weaker than the
///         MetadataUpdateHandler one, in a way nothing caught.
///     </para>
/// </summary>
public class RerenderAllAsyncTests
{
    [Fact]
    public async Task ReExecutesACachedChildsRender()
    {
        var store = NewStore();
        var child = new Counter();
        var session = store.Create(_ => new StaticChildHost(child));

        session.RenderInitialRoot();
        session.RenderInitialRoot();
        Assert.Equal(1, child.RenderCount); // cached: the child's Render didn't re-run

        await store.RerenderAllAsync();

        // These sessions have no socket attached, so the requested render is deferred until one is
        // — the observable effect here is the dirty marking. Render again: with the subtree marked,
        // the cached child re-executes; without it, it replays its previous frame and stays at 1.
        session.RenderInitialRoot();

        Assert.True(child.RenderCount > 1,
            "RerenderAllAsync must bust cached subtrees, or an edit inside one never repaints.");
    }

    [Fact]
    public async Task IsANoOpWithNoSessions()
    {
        var store = NewStore();

        await store.RerenderAllAsync(); // must not throw

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task SwallowsAFaultingSession_AndStillRendersTheRest()
    {
        // Task.WhenAll over every session would surface the first fault to the subscriber's catch
        // and abandon the remaining sessions — one broken tree would freeze every other browser tab.
        var store = NewStore();
        var faulting = store.Create(_ => new StaticChildHost(new Faulting()));
        var healthy = new Counter();
        var healthySession = store.Create(_ => new StaticChildHost(healthy));

        // Render both once so the faulting child is mounted and cached; the next walk over it throws.
        Assert.ThrowsAny<Exception>(() => faulting.RenderInitialRoot());
        healthySession.RenderInitialRoot();
        var before = healthy.RenderCount;

        await store.RerenderAllAsync(); // must not throw

        healthySession.RenderInitialRoot();
        Assert.True(healthy.RenderCount > before,
            "A faulting session must not stop the rest from being marked for repaint.");
    }

    [Fact]
    public async Task BroadcastAsync_WithNoConnectedSockets_IsANoOp()
    {
        // Sessions created directly have no socket attached; SendOutOfBandAsync short-circuits.
        var store = NewStore();
        store.Create(_ => new Counter());

        await store.BroadcastAsync(LivePayload.HotReloadAppliedFrame); // must not throw

        Assert.Equal(1, store.Count);
    }

    private static LiveSessionStore NewStore()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        return new LiveSessionStore(sp.GetRequiredService<IServiceScopeFactory>());
    }

    private sealed class Counter : Component
    {
        public int RenderCount;

        protected override Component? Render()
        {
            RenderCount++;
            return new Span();
        }
    }

    private sealed class Faulting : Component
    {
        protected override Component? Render() => throw new InvalidOperationException("render faulted");
    }

    // Adopts a fixed child through the live context so the child is cached across renders unless it
    // is explicitly marked dirty.
    private sealed class StaticChildHost : Component
    {
        private readonly Component _child;
        public StaticChildHost(Component child) => _child = child;

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _child);
            ctx.NotifyParameters(c, false);
            return c;
        }
    }
}
