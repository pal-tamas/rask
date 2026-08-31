using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// A finished render pass must not still be collecting work.
//
// Dispose can only clear the thread-static slot on whatever thread it runs on, and after an await that
// is routinely not the thread the pass began on — so the scope stays visible to the next render that
// lands on that pool thread. Work then goes into the dead scope while the live pass waits on its own,
// empty, set, and the page is served with a placeholder for data nobody waited for.
//
// Found as an intermittent failure of a server quiescence test that passed every time in isolation:
// it needs enough concurrency for a thread to be recycled between renders, which is the full suite and
// not a single test.
public class QuiescenceScopeStaleThreadTests
{
    [Fact]
    public void TheRunningFlowsScopeBeatsALiveOneLeftOnTheThread()
    {
        // The bug this exists for. A pool thread can still hold ANOTHER render's scope — Begin sets the
        // thread slot, and only a Dispose that happens to run on that same thread clears it. If the
        // thread won, this render's work would be tracked against a stranger's scope, this render's own
        // wave loop would see nothing pending, and the page would be served with a placeholder for data
        // it never waited for — answering 200 while doing it.
        //
        // Nothing needs the thread to win: the one path that loses the AsyncLocal (LifecycleSyncContext's
        // suppressed Task.Run) restores the captured scope with Enter, which sets both slots.
        QuiescenceScope.ResetSyncForTests();
        using var mine = QuiescenceScope.Begin();
        using var stranger = QuiescenceScope.Begin();

        Assert.Same(mine, QuiescenceScope.Resolve(flow: mine, thread: stranger));
    }

    [Fact]
    public void TheThreadIsUsedWhenTheFlowCarriesNothing()
    {
        // The reason the thread slot exists at all: code that crossed an ExecutionContext.SuppressFlow
        // boundary has no AsyncLocal to read. Preferring the flow must not mean ignoring the thread.
        QuiescenceScope.ResetSyncForTests();
        using var thread = QuiescenceScope.Begin();

        Assert.Same(thread, QuiescenceScope.Resolve(flow: null, thread: thread));
    }

    [Fact]
    public void ADeadScopeInEitherSlotIsNeverResolved()
    {
        QuiescenceScope.ResetSyncForTests();
        var deadFlow = QuiescenceScope.Begin();
        deadFlow.Dispose();
        var liveThread = QuiescenceScope.Begin();

        // A finished render must not keep collecting, whichever slot still points at it.
        Assert.Same(liveThread, QuiescenceScope.Resolve(deadFlow, liveThread));
        Assert.Null(QuiescenceScope.Resolve(deadFlow, deadFlow));

        liveThread.Dispose();
    }

    [Fact]
    public void ADisposedScopeIsNotCurrent()
    {
        QuiescenceScope.ResetSyncForTests();

        var scope = QuiescenceScope.Begin();
        Assert.Same(scope, QuiescenceScope.Current);

        scope.Dispose();

        Assert.Null(QuiescenceScope.Current);
    }

    [Fact]
    public void AScopeLeftOnTheThreadByAnotherPassIsNotCurrent()
    {
        // The real shape: the pass ends somewhere else, so nothing clears THIS thread's slot. Modelled
        // by disposing from another thread, which is exactly what an await continuation does.
        QuiescenceScope.ResetSyncForTests();

        var scope = QuiescenceScope.Begin();
        var other = new Thread(scope.Dispose);
        other.Start();
        other.Join();

        // The slot on this thread still points at it — reading must not hand it back, and must clear it.
        Assert.Null(QuiescenceScope.Current);

        // And a fresh pass on the same thread gets its own scope, not the corpse.
        var next = QuiescenceScope.Begin();
        Assert.Same(next, QuiescenceScope.Current);
        next.Dispose();
    }
}
