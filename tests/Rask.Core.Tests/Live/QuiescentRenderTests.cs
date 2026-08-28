using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// The wave loop, driven with no host at all — which is the whole reason it moved into Core. A server
// answering a GET and a build-time prerender of an app that has no server want identical behaviour, and
// before this they could not share it.
public class QuiescentRenderTests
{
    [Fact]
    public async Task WorkStartedByARenderIsAwaitedAndTheNextWaveIsReturned()
    {
        QuiescenceScope.ResetSyncForTests();
        var gate = new TaskCompletionSource();
        var ready = false;

        var result = await QuiescentRender.RunAsync(
            _ =>
            {
                if (!ready)
                {
                    // What OnMountAsync does: start work, render the placeholder meanwhile.
                    QuiescenceScope.Current!.TrackExternal(Settle(gate, () => ready = true));
                    return "loading";
                }

                return "loaded";
            },
            TimeSpan.FromSeconds(5));

        Assert.Equal("loaded", result.Html);
        Assert.False(result.TimedOut);
        Assert.Equal(1, result.Waves);
    }

    [Fact]
    public async Task TheFirstWaveIsNotPublishOnlyAndEveryLaterOneIs()
    {
        // Honouring this is what stops each wave re-firing OnRendered on everything the previous wave
        // already rendered, which multiplies lifecycle callbacks per wave rather than adding to them.
        QuiescenceScope.ResetSyncForTests();
        var seen = new List<bool>();
        var rounds = 0;

        await QuiescentRender.RunAsync(
            publishOnly =>
            {
                seen.Add(publishOnly);
                if (rounds++ < 2)
                {
                    QuiescenceScope.Current!.TrackExternal(Task.Delay(1));
                }

                return "html";
            },
            TimeSpan.FromSeconds(5));

        Assert.Equal([false, true, true], seen);
    }

    [Fact]
    public async Task WorkThatNeverSettlesGivesUpOnTheBudgetAndSaysSo()
    {
        // The caller has to know: a page served with work still in flight cannot be a static document,
        // because nothing is left running that would ever replace its placeholder.
        QuiescenceScope.ResetSyncForTests();

        var result = await QuiescentRender.RunAsync(
            _ =>
            {
                QuiescenceScope.Current!.TrackExternal(new TaskCompletionSource().Task);
                return "still-loading";
            },
            TimeSpan.FromMilliseconds(120));

        Assert.True(result.TimedOut);
        Assert.Equal("still-loading", result.Html);
    }

    [Fact]
    public async Task BlockedWorkIsNotWaitedFor()
    {
        // Waiting for work that cannot complete here spends the entire budget to learn nothing. The
        // server's case is a queued JS call, which completes only once a socket exists — so this must
        // return promptly rather than after the budget, and must NOT be reported as a timeout.
        QuiescenceScope.ResetSyncForTests();
        var started = DateTime.UtcNow;

        var result = await QuiescentRender.RunAsync(
            _ =>
            {
                QuiescenceScope.Current!.TrackExternal(new TaskCompletionSource().Task);
                return "blocked";
            },
            TimeSpan.FromSeconds(30),
            isBlocked: () => true);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "it waited for work it was told could not finish");
        Assert.False(result.TimedOut);
        Assert.Equal("blocked", result.Html);
    }

    [Fact]
    public async Task ARenderWhoseEveryWaveStartsMoreWorkIsCapped()
    {
        // Otherwise a page that always has something pending renders until the budget, and the response
        // grows with every wave.
        QuiescenceScope.ResetSyncForTests();
        var waves = 0;

        var result = await QuiescentRender.RunAsync(
            _ =>
            {
                waves++;
                QuiescenceScope.Current!.TrackExternal(Task.Delay(1));
                return "endless";
            },
            TimeSpan.FromSeconds(30),
            maxWaves: 3);

        Assert.True(result.TimedOut);
        Assert.Equal(3, result.Waves);
        Assert.Equal(4, waves); // the first render, then three capped waves
    }

    private static async Task Settle(TaskCompletionSource gate, Action then)
    {
        gate.TrySetResult();
        await gate.Task.ConfigureAwait(false);
        then();
    }
}
