using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// #1074: a child whose hook awaits with ConfigureAwait(false) was occasionally served as its PLACEHOLDER at 200. The wave
// loop tracked the child's work in time; what went wrong was the child's render clearing its dirty flag AFTER reading its
// state, so a hook that resumed on the pool while that render was mid-flight had its StateHasChanged wiped, the stale
// output was cached, and the next wave replayed it and found nothing pending (#1067's lost update, fixed in Component).
//
// RenderSkipTests pins the flag order for one render. This pins the whole wave loop — the path a GET and a prerender take
// — with the race held open at the exact point it used to be lost, instead of hoping load hits it.
public partial class QuiescentRenderMidRenderPaintTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_ConfigureAwaitFalse_hook_resolving_mid_render_is_served_loaded()
    {
        QuiescenceScope.ResetSyncForTests();
        var sp = RenderHarness.EmptyServices();
        using var reading = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var child = new MidRenderChild(reading, resume);
        var host = new Host(child);

        var run = Task.Run(() => QuiescentRender.RunAsync(
            publishOnly => host.RenderAsLiveRoot(sp, publishOnly), TimeSpan.FromSeconds(10)));

        // The first wave is inside the child's Render(), having read the placeholder state.
        Assert.True(reading.Wait(TimeSpan.FromSeconds(10)), "the mount wave never rendered the child");

        // The hook resumes on the pool, sets the loaded value and asks for a repaint — all while that render is paused.
        child.Gate.SetResult();
        Assert.True(
            SpinWait.SpinUntil(() => child.IsRenderRequestedForTest, TimeSpan.FromSeconds(10)),
            "the resumed hook never asked for a repaint");
        resume.Set();

        var result = await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("child-loaded", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("child-loading", result.Html, StringComparison.Ordinal);
        Assert.False(result.TimedOut);
    }

    private sealed class Host(Component child) : Component
    {
        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => child);
            ctx.NotifyParameters(c, false);
            return c;
        }
    }

    // Reads its state, then — on its first render only — waits inside Render() for the test, so the hook can resume and
    // request a repaint at the one point the lost update needs.
    private sealed class MidRenderChild(ManualResetEventSlim reading, ManualResetEventSlim resume) : Component
    {
        internal readonly TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile string? _value;
        private volatile bool _paused;

        protected override async Task Mount()
        {
            await Gate.Task.ConfigureAwait(false);
            _value = "child-loaded";
        }

        protected override Component? Render()
        {
            var shown = _value ?? "child-loading";
            if (!_paused)
            {
                _paused = true;
                reading.Set();
                resume.Wait(TimeSpan.FromSeconds(10));
            }

            return Span[shown];
        }
    }
}
