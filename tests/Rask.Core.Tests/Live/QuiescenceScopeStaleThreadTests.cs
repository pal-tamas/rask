using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// A render pass's scope must be visible to that pass and to nothing else.
//
// The scope used to live in a ThreadStatic as well as an AsyncLocal. After an await the thread goes
// back to the pool still holding the pass that began on it, so the next render on that thread — a
// finished pass's corpse, or a stranger that is still waiting — was found by work that did not belong
// to it. Work then went into the wrong scope while the right pass waited on its own, empty, set.
//
// Found twice as intermittent failures that passed every time in isolation (a server quiescence test,
// then #1108's PageMetaTests): both need enough concurrency for a thread to be recycled between
// renders, which is the full suite and not a single test.
public class QuiescenceScopeStaleThreadTests
{
    [Fact]
    public void A_live_scope_begun_by_another_pass_on_this_thread_is_not_current_once_its_flow_has_gone()
    {
        // #1108. QuiescentRender.RunAsync calls Begin on a pool thread and then awaits: the runtime
        // restores the thread's ExecutionContext when the async method yields, so the AsyncLocal is
        // gone from that thread — but a thread slot stayed behind, pointing at a render that was still
        // waiting. A scope-less synchronous render landing there (Test.Render in a parallel test
        // class) then tracked its hooks into the stranger, whose wave loop kept finding new work until
        // the 16-wave cap served it "did not settle" in half a second.
        QuiescenceScope? stranger = null;
        QuiescenceScope? observed = null;
        var thread = new Thread(() =>
        {
            BeginTheWayRunAsyncDoes(scope => stranger = scope).GetAwaiter().GetResult();
            observed = QuiescenceScope.Current;
        });
        thread.Start();
        thread.Join();

        Assert.NotNull(stranger);
        Assert.Null(observed);
        stranger.Dispose();
    }

    [Fact]
    public async Task Enter_restores_a_scope_for_code_that_crossed_SuppressFlow()
    {
        // The one path that loses the AsyncLocal: LifecycleSyncContext's suppressed Task.Run. It is
        // handed the scope captured on the walk and must still find it, and leave nothing behind.
        QuiescenceScope.ResetSyncForTests();
        using var captured = QuiescenceScope.Begin();
        QuiescenceScope? unrestored = captured;
        QuiescenceScope? inside = null;
        QuiescenceScope? after = null;

        Task work;
        using (ExecutionContext.SuppressFlow())
        {
            work = Task.Run(() =>
            {
                unrestored = QuiescenceScope.Current;
                using (QuiescenceScope.Enter(captured))
                {
                    inside = QuiescenceScope.Current;
                }

                after = QuiescenceScope.Current;
            });
        }

        await work;

        Assert.Null(unrestored);
        Assert.Same(captured, inside);
        Assert.Null(after);
    }

    [Fact]
    public void A_disposed_scope_is_not_current()
    {
        QuiescenceScope.ResetSyncForTests();

        var scope = QuiescenceScope.Begin();
        Assert.Same(scope, QuiescenceScope.Current);

        scope.Dispose();

        Assert.Null(QuiescenceScope.Current);
    }

    [Fact]
    public void A_scope_disposed_on_another_thread_is_not_current()
    {
        // The real shape: the pass ends somewhere else, which is exactly what an await continuation
        // does. The flow here still carries it, and must not hand it back.
        QuiescenceScope.ResetSyncForTests();

        var scope = QuiescenceScope.Begin();
        var other = new Thread(scope.Dispose);
        other.Start();
        other.Join();

        Assert.Null(QuiescenceScope.Current);

        // And a fresh pass gets its own scope, not the corpse.
        var next = QuiescenceScope.Begin();
        Assert.Same(next, QuiescenceScope.Current);
        next.Dispose();
    }

    private static async Task BeginTheWayRunAsyncDoes(Action<QuiescenceScope> keep)
    {
        keep(QuiescenceScope.Begin());
        await Task.CompletedTask;
    }
}
