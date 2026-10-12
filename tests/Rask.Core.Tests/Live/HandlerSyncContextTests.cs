using System.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public class HandlerSyncContextTests
{
    [Fact]
    public async Task Draining_with_no_pending_posts_completes_immediately()
    {
        var ctx = new HandlerSyncContext(() => Task.CompletedTask);

        await ctx.DrainAsync();
    }

    [Fact]
    public async Task With_no_repaints_withheld_a_post_renders_before_and_after_each_continuation()
    {
        var renderCount = 0;
        var ctx = new HandlerSyncContext(() =>
        {
            Interlocked.Increment(ref renderCount);
            return Task.CompletedTask;
        });
        var ran = 0;

        ctx.Post(_ => Interlocked.Increment(ref ran), null);
        ctx.Post(_ => Interlocked.Increment(ref ran), null);

        await ctx.DrainAsync();

        Assert.Equal(2, ran);
        Assert.Equal(4, renderCount);
    }

    [Fact]
    public async Task A_post_made_before_the_repaints_open_runs_its_continuation_and_repaints_nothing()
    {
        var renderCount = 0;
        var ctx = new HandlerSyncContext(
            () =>
            {
                Interlocked.Increment(ref renderCount);
                return Task.CompletedTask;
            },
            repaintsFrom: Stopwatch.GetTimestamp() + (5 * 60 * Stopwatch.Frequency));
        var ran = 0;

        ctx.Post(_ => Interlocked.Increment(ref ran), null);
        ctx.Post(_ => Interlocked.Increment(ref ran), null);
        await ctx.DrainAsync();

        Assert.Equal(2, ran);
        Assert.Equal(0, renderCount);
    }

    [Fact]
    public async Task A_post_made_once_the_repaints_have_opened_renders_before_and_after_its_continuation()
    {
        var renderCount = 0;
        var ctx = new HandlerSyncContext(
            () =>
            {
                Interlocked.Increment(ref renderCount);
                return Task.CompletedTask;
            },
            repaintsFrom: Stopwatch.GetTimestamp() - 1);

        ctx.Post(static _ => { }, null);
        await ctx.DrainAsync();

        Assert.Equal(2, renderCount);
    }

    [Fact]
    public void A_copy_withholds_its_repaints_until_the_same_moment()
    {
        var renderCount = 0;
        var ctx = new HandlerSyncContext(
            () =>
            {
                renderCount++;
                return Task.CompletedTask;
            },
            repaintsFrom: Stopwatch.GetTimestamp() + (5 * 60 * Stopwatch.Frequency));

        ctx.CreateCopy().Send(static _ => { }, null);

        Assert.Equal(0, renderCount);
    }

    [Fact]
    public void A_send_blocks_until_the_callback_completes()
    {
        var ctx = new HandlerSyncContext(() => Task.CompletedTask);
        var ran = false;

        ctx.Send(_ => ran = true, null);

        Assert.True(ran);
    }

    [Fact]
    public async Task A_post_installs_the_context_on_the_continuation_thread()
    {
        var ctx = new HandlerSyncContext(() => Task.CompletedTask);
        SynchronizationContext? observed = null;
        var done = new TaskCompletionSource();

        ctx.Post(_ =>
        {
            observed = SynchronizationContext.Current;
            done.SetResult();
        }, null);

        await done.Task;
        await ctx.DrainAsync();

        Assert.Same(ctx, observed);
    }

    [Fact]
    public void CreateCopy_gives_an_independent_instance_with_the_same_render()
    {
        var renderCount = 0;
        var ctx = new HandlerSyncContext(() =>
        {
            renderCount++;
            return Task.CompletedTask;
        });

        var copy = ctx.CreateCopy();

        Assert.NotSame(ctx, copy);
        Assert.IsType<HandlerSyncContext>(copy);
        copy.Send(_ => { }, null);
        Assert.Equal(2, renderCount);
    }

    [Fact]
    public async Task Draining_also_drains_tasks_posted_during_the_drain()
    {
        // Re-entrancy invariant: a callback runs with this context installed, so any Post it makes
        // (directly, or via an awaited continuation) enqueues a NEW task while DrainAsync is mid-
        // flight. The while-loop re-snapshots _pending after each WhenAll, so the late task must be
        // awaited too — DrainAsync returns only when the whole cascade has settled.
        var ctx = new HandlerSyncContext(() => Task.CompletedTask);
        var ran = new List<int>();
        var gate = new object();
        var second = new TaskCompletionSource();

        ctx.Post(_ =>
        {
            lock (gate)
            {
                ran.Add(1);
            }

            // Re-entrant Post during the first callback's execution (before its task completes).
            ctx.Post(__ =>
            {
                lock (gate)
                {
                    ran.Add(2);
                }

                second.SetResult();
            }, null);
        }, null);

        await ctx.DrainAsync();

        // If DrainAsync stopped after the first task it would return before the second ran.
        Assert.True(second.Task.IsCompleted, "the task posted during drain must have been awaited");
        Assert.Contains(1, ran);
        Assert.Contains(2, ran);
    }
}
