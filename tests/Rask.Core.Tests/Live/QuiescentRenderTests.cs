using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// The wave loop, driven with no host at all — which is the whole reason it moved into Core. A server
// answering a GET and a build-time prerender of an app that has no server want identical behaviour, and
// before this they could not share it.
public class QuiescentRenderTests
{
    [Fact]
    public async Task Work_started_by_a_render_is_awaited_and_the_next_wave_is_returned()
    {
        QuiescenceScope.ResetSyncForTests();
        var gate = new TaskCompletionSource();
        var ready = false;

        var result = await QuiescentRender.Run(
            _ =>
            {
                if (!ready)
                {
                    // What Mount does: start work, render the placeholder meanwhile.
                    QuiescenceScope.Current!.TrackExternal(Settle(gate, () => ready = true));
                    return "loading";
                }

                return "loaded";
            },
            TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("loaded", result.Html);
        Assert.False(result.TimedOut);
        Assert.Equal(1, result.Waves);
    }

    [Fact]
    public async Task A_render_asked_for_during_a_wave_earns_one_more_wave()
    {
        // A page that names itself to its layout as it mounts: nothing is pending, yet the layout the wave
        // rendered a moment earlier is already out of date.
        QuiescenceScope.ResetSyncForTests();
        var asked = false;
        var renders = 0;

        var result = await QuiescentRender.Run(
            _ =>
            {
                asked = renders++ == 0;
                return asked ? "stale" : "current";
            },
            TimeSpan.FromSeconds(5), renderRequested: () => asked, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("current", result.Html);
        Assert.False(result.TimedOut);
        Assert.Equal(1, result.Waves);
    }

    [Fact]
    public async Task A_page_that_asks_for_a_render_on_every_wave_is_cut_off_without_timing_out()
    {
        QuiescenceScope.ResetSyncForTests();
        var renders = 0;

        var result = await QuiescentRender.Run(
            _ => $"render {++renders}",
            TimeSpan.FromSeconds(5), renderRequested: () => true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1 + QuiescentRender.MaxRequestedWaves, renders);
        Assert.Equal(QuiescentRender.MaxRequestedWaves, result.Waves);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task A_wave_nobody_asked_to_repeat_is_the_only_one()
    {
        QuiescenceScope.ResetSyncForTests();
        var renders = 0;

        var result = await QuiescentRender.Run(
            _ => $"render {++renders}",
            TimeSpan.FromSeconds(5), renderRequested: () => false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("render 1", result.Html);
        Assert.Equal(0, result.Waves);
    }

    [Fact]
    public async Task The_first_wave_is_not_publish_only_and_every_later_one_is()
    {
        // Honouring this is what stops each wave re-firing OnRendered on everything the previous wave
        // already rendered, which multiplies lifecycle callbacks per wave rather than adding to them.
        QuiescenceScope.ResetSyncForTests();
        var seen = new List<bool>();
        var rounds = 0;

        await QuiescentRender.Run(
            publishOnly =>
            {
                seen.Add(publishOnly);
                if (rounds++ < 2)
                {
                    QuiescenceScope.Current!.TrackExternal(Task.Delay(1));
                }

                return "html";
            },
            TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([false, true, true], seen);
    }

    [Fact]
    public async Task Work_that_never_settles_gives_up_on_the_budget_and_says_so()
    {
        // The caller has to know: a page served with work still in flight cannot be a static document,
        // because nothing is left running that would ever replace its placeholder.
        QuiescenceScope.ResetSyncForTests();

        var result = await QuiescentRender.Run(
            _ =>
            {
                QuiescenceScope.Current!.TrackExternal(new TaskCompletionSource().Task);
                return "still-loading";
            },
            TimeSpan.FromMilliseconds(120), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.TimedOut);
        Assert.Equal("still-loading", result.Html);
    }

    [Fact]
    public async Task Blocked_work_is_not_waited_for()
    {
        // Waiting for work that cannot complete here spends the entire budget to learn nothing. The
        // server's case is a queued JS call, which completes only once a socket exists — so this must
        // return promptly rather than after the budget, and must NOT be reported as a timeout.
        QuiescenceScope.ResetSyncForTests();
        var started = DateTime.UtcNow;

        var result = await QuiescentRender.Run(
            _ =>
            {
                QuiescenceScope.Current!.TrackExternal(new TaskCompletionSource().Task);
                return "blocked";
            },
            TimeSpan.FromSeconds(30),
            isBlocked: () => true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "it waited for work it was told could not finish");
        Assert.False(result.TimedOut);
        Assert.Equal("blocked", result.Html);
    }

    [Fact]
    public async Task A_render_whose_every_wave_starts_more_work_is_capped()
    {
        // Otherwise a page that always has something pending renders until the budget, and the response
        // grows with every wave.
        QuiescenceScope.ResetSyncForTests();
        var waves = 0;

        var result = await QuiescentRender.Run(
            _ =>
            {
                waves++;
                QuiescenceScope.Current!.TrackExternal(Task.Delay(1));
                return "endless";
            },
            TimeSpan.FromSeconds(30),
            maxWaves: 3, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.TimedOut);
        Assert.Equal(3, result.Waves);
        Assert.Equal(4, waves); // the first render, then three capped waves
    }

    [Fact]
    public async Task Cancelling_abandons_the_wait_rather_than_returning_markup()
    {
        // A render nobody is waiting for any more — a background refresh when the host stops — must not
        // hold shutdown for the rest of its budget, and must not hand back a placeholder that reads like
        // a result somebody could store.
        QuiescenceScope.ResetSyncForTests();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var started = DateTime.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => QuiescentRender.Run(
            _ =>
            {
                QuiescenceScope.Current!.TrackExternal(new TaskCompletionSource().Task);
                return "still-loading";
            },
            TimeSpan.FromSeconds(30),
            cancellationToken: cancel.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "cancellation waited out the budget");
    }

    [Fact]
    public async Task An_already_cancelled_token_renders_nothing()
    {
        QuiescenceScope.ResetSyncForTests();
        var rendered = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => QuiescentRender.Run(
            _ =>
            {
                rendered = true;
                return "html";
            },
            TimeSpan.FromSeconds(5),
            cancellationToken: new CancellationToken(canceled: true)));

        Assert.False(rendered);
    }

    private static async Task Settle(TaskCompletionSource gate, Action then)
    {
        gate.TrySetResult();
        await gate.Task.ConfigureAwait(false);
        then();
    }
}
