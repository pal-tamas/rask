using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public class HandlerSyncContextTests
{
    [Fact]
    public async Task DrainAsync_NoPendingPosts_CompletesImmediately()
    {
        var ctx = new HandlerSyncContext(() => Task.CompletedTask);

        await ctx.DrainAsync();
    }

    [Fact]
    public async Task Post_RendersBeforeAndAfter_EachContinuation()
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
    public void Send_BlocksUntilCallbackCompletes()
    {
        var ctx = new HandlerSyncContext(() => Task.CompletedTask);
        var ran = false;

        ctx.Send(_ => ran = true, null);

        Assert.True(ran);
    }

    [Fact]
    public async Task Post_InstallsContextOnContinuationThread()
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
    public void CreateCopy_ReturnsIndependentInstance_SameRender()
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
}
