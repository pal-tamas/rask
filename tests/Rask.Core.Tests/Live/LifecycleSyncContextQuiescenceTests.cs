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
        var context = new LifecycleSyncContext(Probe, scope);

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

        new LifecycleSyncContext(Probe, scope).Post(_ => throw new InvalidOperationException("boom"), state: null);

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
        new LifecycleSyncContext(Probe, quiescence: null).Post(_ => done.Set(), state: null);

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "the posted continuation never ran");
        Assert.Null(QuiescenceScope.Current);
    }

    [Fact]
    public async Task The_gate_survives_a_continuation_posted_from_a_foreign_thread()
    {
        // The way #932 actually escaped: Post could not see the pass it belonged to.
        //
        // The runtime restores an awaiter's captured ExecutionContext around the CONTINUATION, not
        // around the SynchronizationContext.Post that schedules it — so an AsyncLocal read inside
        // Post observes the thread that finished the awaited task, never the render. Post therefore
        // resolved the scope through QuiescenceScope's thread-static fallback and got it only when
        // the timer happened to fire on the very thread Begin() had run on. Every other time it got
        // null, registered no gate, and left the wave loop free to snapshot in the one-line window
        // between the hook's Task completing and the StateHasChanged that paints its data.
        //
        // Nothing here is timing-dependent: the walk runs on a dedicated thread, so the pool thread
        // that completes the hook's await CANNOT be carrying that thread-static. Without the scope
        // being handed in from the walk, this fails every time.
        QuiescenceScope.ResetSyncForTests();

        ForeignThreadHookProbe component = ForeignThreadHookProbe;
        QuiescenceScope? scope = null;

        var walk = new Thread(() =>
        {
            scope = QuiescenceScope.Begin();
            component.RaiseLifecycleBeforeRender(propsChanged: false);

            // The first wave's snapshot: takes the hook's own Task and leaves _pending empty, so
            // anything the assertion below finds can only be the gate Post registered.
            scope.TrySnapshotPending(out _);
        })
        {
            IsBackground = true,
        };

        walk.Start();
        walk.Join(TimeSpan.FromSeconds(10));

        Assert.NotNull(scope);
        using var pass = scope;

        // Resume the hook. Its continuation is posted from a pool thread — one that holds neither
        // slot, which is every thread but the dead one above.
        component.Resume.SetResult();

        Assert.True(
            component.Entered.Wait(TimeSpan.FromSeconds(10)),
            "the hook never resumed");

        // Asked at the worst possible moment, exactly as the wave loop would: the hook has resumed
        // and its data has landed, but the render that would show it has not happened.
        Assert.True(pass.TrySnapshotPending(out var batch), "the scope reported nothing pending");
        Assert.All(batch, t => Assert.False(t.IsCompleted));

        component.Release.Set();

        await Task.WhenAll(batch).WaitAsync(TimeSpan.FromSeconds(10));
    }
}

/// <summary>
///     A component whose <c>OnMountAsync</c> parks on a gate the test opens from another thread, then
///     holds inside the continuation so the pass can be inspected mid-flight.
/// </summary>
public sealed partial class ForeignThreadHookProbe : Component
{
    internal readonly ManualResetEventSlim Entered = new(false);
    internal readonly ManualResetEventSlim Release = new(false);

    internal readonly TaskCompletionSource Resume =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task OnMountAsync()
    {
        await Resume.Task;
        Entered.Set();
        Release.Wait(TimeSpan.FromSeconds(10));
    }

    protected override Component? Render() => null;
}

/// <summary>A component that renders nothing; only its identity matters to the sync context.</summary>
public sealed partial class Probe : Component
{
    protected override Component? Render() => null;
}
