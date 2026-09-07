using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// The one-line gap that served a page with its placeholder (#932).
//
// LifecycleSyncContext.Post schedules the continuation of an async lifecycle hook. The user's method
// body returns inside that continuation, which transitions the HOOK's Task to Completed — one line
// BEFORE the StateHasChanged() that paints the resolved data. QuiescentRender.RunAsync waits on the
// hook Task, so it could wake in that window, re-render, find nothing pending, and serve.
//
// The result was a page carrying its placeholder, at 200, well inside the budget, with nothing marked
// timed out and no fault raised anywhere. It reproduced roughly one run in six under gate load and
// never in isolation — a stress harness that ran it 12 times under four concurrent builds could not
// provoke it either way. So the invariant is pinned here instead of by re-running the race: what has
// to be true is that the scope still holds pending work for as long as the render is outstanding, and
// that is a statement about ORDERING, which is testable, rather than about timing, which is not.
public partial class LifecycleSyncContextQuiescenceTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task Post_registers_work_that_outlives_the_hooks_own_task()
    {
        QuiescenceScope.ResetSyncForTests();
        using var scope = QuiescenceScope.Begin();

        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var context = new LifecycleSyncContext(Probe);

        // Stands in for the user's continuation: it is INSIDE this callback that the hook's own Task
        // would complete, and StateHasChanged has not run until it returns.
        context.Post(
            _ =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            },
            state: null);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "the posted continuation never ran");

        // The wave loop's own question, asked at the worst possible moment: mid-continuation, exactly
        // where the hook Task has completed but the render has not happened.
        Assert.True(scope.TrySnapshotPending(out var batch), "the scope reported nothing pending");
        Assert.All(batch, t => Assert.False(t.IsCompleted));

        release.Set();

        // And it must close, or an initial render would wait out its whole budget on every hook.
        await Task.WhenAll(batch).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_throwing_continuation_still_opens_the_gate()
    {
        // Otherwise a hook that throws leaves the initial render waiting on something that will never
        // complete, turning a component fault into a page that hangs until the budget expires. The
        // fault itself is already routed to the nearest ErrorBoundary by the caller.
        QuiescenceScope.ResetSyncForTests();
        using var scope = QuiescenceScope.Begin();

        new LifecycleSyncContext(Probe).Post(_ => throw new InvalidOperationException("boom"), state: null);

        Assert.True(scope.TrySnapshotPending(out var batch), "the scope reported nothing pending");

        await Task.WhenAll(batch).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Post_without_a_scope_registers_nothing()
    {
        // Every interactive render after the first runs with no quiescence scope at all, and that is by
        // far the common path — it must not allocate a gate nobody waits on.
        QuiescenceScope.ResetSyncForTests();

        using var done = new ManualResetEventSlim(false);
        new LifecycleSyncContext(Probe).Post(_ => done.Set(), state: null);

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "the posted continuation never ran");
        Assert.Null(QuiescenceScope.Current);
    }
}

/// <summary>A component that renders nothing; only its identity matters to the sync context.</summary>
public sealed partial class Probe : Component
{
    protected override Component? Render() => null;
}
