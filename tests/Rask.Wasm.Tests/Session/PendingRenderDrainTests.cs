using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// A StateHasChanged raised while a dispatch holds its scope parks in _pendingRenderInScope. The
// coalescing loop drains that flag, but only up to the point where it settles — a request arriving
// afterwards, during the noop guard or the frame emit, was left parked, and the NEXT dispatch's
// coalescing loop opens by clearing the flag. So it was discarded, not deferred (#986).
//
// That window is where a first-paint fetch's continuation lands on a hard load: the request settles
// in a couple of milliseconds while the initial render is still emitting, so the state was set and
// the page kept its spinner (#972). The trace of a failing run shows the fetch returning 200 while
// the DOM still holds the "Loading…" markup and no <article> at all.
//
// The production trigger is the scope-exit `finally`. That window contains no user hook — which is
// exactly why the drop was invisible — so these tests drive the drain directly rather than staging
// browser timing they cannot control.
[Collection("WasmSession")]
public class PendingRenderDrainTests : ResettingTestBase
{
    [Fact]
    public async Task RequestParkedWhileInScope_IsDrainedIntoARealRender()
    {
        var app = new RenderCountingApp();
        var (session, _) = NewSession(_ => app);

        await session.InitialRenderAsync();
        var afterInitial = app.RenderCount;
        Assert.True(afterInitial > 0, "the initial render should have rendered the app at least once");

        // Stage the window: the dispatch is still in scope, so the request only parks.
        session.InHandlerScope = true;
        app.StateHasChanged();
        Assert.Equal(afterInitial, app.RenderCount);

        // Leaving the scope must hand the parked request a render of its own.
        session.InHandlerScope = false;
        await session.DrainRenderRequestedAfterScope();

        Assert.True(app.RenderCount > afterInitial,
            $"a repaint requested inside the scope was dropped: still {app.RenderCount} renders.");
    }

    [Fact]
    public async Task NothingParked_DrainIsANoop()
    {
        var app = new RenderCountingApp();
        var (session, _) = NewSession(_ => app);

        await session.InitialRenderAsync();
        var afterInitial = app.RenderCount;

        await session.DrainRenderRequestedAfterScope();

        Assert.Equal(afterInitial, app.RenderCount);
    }
}
