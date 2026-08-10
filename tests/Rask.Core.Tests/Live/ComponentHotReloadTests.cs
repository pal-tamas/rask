using Rask.Core.HotReload;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// The C# Hot Reload → live re-render seam: a component-code edit under `dotnet watch` must re-execute
// every component's Render() (busting cached subtrees) and re-render every tracked session. These pin the
// two halves — MarkSubtreeDirtyForHotReload forces a cached child to re-run, and
// RerenderAllForHotReloadAsync / the MetadataUpdateHandler drive registered sessions (resiliently, and
// only when registered).
//
// Shares the ScopedAssets collection with HotReloadPhaseTests. Both trigger a hot-reload apply, and
// the tracked-session list is a process-wide static — so run in parallel they repaint each other's
// sessions and both classes' render counts come out high.
[Collection("ScopedAssets")]
public class ComponentHotReloadTests
{
    [Fact]
    public void MarkSubtreeDirtyForHotReload_ForcesCachedChildToReExecute()
    {
        var sp = RenderHarness.EmptyServices();
        var child = new Counter();
        var host = new StaticChildHost(child);

        host.RenderAsLiveRoot(sp);
        host.RenderAsLiveRoot(sp);
        Assert.Equal(1, child.RenderCount); // cached: the child's Render didn't re-run on the 2nd pass

        Component.MarkSubtreeDirtyForHotReload(host);
        host.RenderAsLiveRoot(sp);

        Assert.Equal(2, child.RenderCount); // marked dirty → re-executes against the (would-be) new IL
    }

    [Fact]
    public async Task RerenderAllForHotReload_RequestsRenderOnRegisteredSessions()
    {
        var session = new TestLiveSession(new Counter(), RenderHarness.EmptyServices());
        session.RegisterForHotReload();

        await LiveSessionBase.RerenderAllForHotReloadAsync();

        Assert.Equal(1, session.RenderRequests);
        GC.KeepAlive(session);
    }

    [Fact]
    public async Task RerenderAllForHotReload_DoesNotTouchUnregisteredSessions()
    {
        var session = new TestLiveSession(new Counter(), RenderHarness.EmptyServices());
        // deliberately not registered

        await LiveSessionBase.RerenderAllForHotReloadAsync();

        Assert.Equal(0, session.RenderRequests);
        GC.KeepAlive(session);
    }

    [Fact]
    public async Task RerenderAllForHotReload_SwallowsAFaultingSession_AndStillRendersTheRest()
    {
        var faulting = new TestLiveSession(new Counter(), RenderHarness.EmptyServices()) { Throw = true };
        var healthy = new TestLiveSession(new Counter(), RenderHarness.EmptyServices());
        faulting.RegisterForHotReload();
        healthy.RegisterForHotReload();

        await LiveSessionBase.RerenderAllForHotReloadAsync(); // must not throw despite `faulting`

        Assert.Equal(1, healthy.RenderRequests);
        GC.KeepAlive(faulting);
        GC.KeepAlive(healthy);
    }

    [Fact]
    public async Task UpdateApplication_ReRendersRegisteredSessions()
    {
        var session = new TestLiveSession(new Counter(), RenderHarness.EmptyServices());
        session.RegisterForHotReload();

        // The repaint phase is dispatched off the hot-reload agent's thread, so wait for the
        // coordinator's own completion signal rather than sleeping.
        await RunUpdateApplicationAsync();

        Assert.Equal(1, session.RenderRequests);
        GC.KeepAlive(session);
    }

    internal static async Task RunUpdateApplicationAsync()
    {
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnApplied() => applied.TrySetResult();

        RaskHotReload.Applied += OnApplied;
        try
        {
            RaskHotReloadHandler.UpdateApplication(updatedTypes: null);
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            RaskHotReload.Applied -= OnApplied;
        }
    }

    private sealed class Counter : Component
    {
        public int RenderCount;

        protected override Component? Render()
        {
            RenderCount++;
            return Div;
        }
    }

    // Mirrors RenderSkipTests' host: adopts a fixed child through the live context so the child is cached
    // across renders unless it's marked dirty.
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

    // Minimal concrete LiveSessionBase that records render requests instead of touching a transport.
    private sealed class TestLiveSession : LiveSessionBase
    {
        public int RenderRequests;
        public bool Throw;

        public TestLiveSession(Component view, IServiceProvider services)
            : base(view, services, LiveDiffMode.Auto)
        {
        }

        protected override Task RequestRenderInternalAsync(bool publishOnly)
        {
            RenderRequests++;
            if (Throw)
            {
                throw new InvalidOperationException("session render faulted");
            }

            return Task.CompletedTask;
        }

        protected override Task RenderInScopeCoreAsync() => Task.CompletedTask;

        // No transport in this test double — the render-request recorder never emits a frame.
        protected override ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame) => default;
    }
}
