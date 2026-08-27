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
