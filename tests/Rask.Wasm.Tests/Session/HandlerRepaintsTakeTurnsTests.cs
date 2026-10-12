using System.Text;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Session;

// A handler that awaits is repainted twice while it runs: once when it first yields, and once before its
// continuation. The two are asked for from different places, and on a host with more than one thread they
// walked the tree together — the walk threw, and the handler's continuation (or the handler) went with it, so
// a `await Save(); Go.To(…)` now and then never navigated. They take turns.
[Collection("WasmSession")]
public sealed class HandlerRepaintsTakeTurnsTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task The_repaint_before_a_handlers_continuation_waits_for_the_one_still_walking_the_tree()
    {
        var app = new HeldWalkApp();
        var (session, _) = NewSession(_ => app);
        var click = Regex.Match(
            Encoding.UTF8.GetString(await session.InitialRenderAsync()), "data-rask-on-click=..([^\"\\\\]+)",
            RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
        app.HoldWalks();

        var dispatch = session.DispatchAsync(Utf8($$"""{"id":"{{click}}","type":"click"}"""));
        await WaitFor.True(() => app.Walking == 1, "the handler's first repaint to be inside the walk");
        app.Saved.SetResult();
        await WaitFor.True(
            () => session.InScopeRendersWaiting == 1 || app.MostWalkingAtOnce > 1,
            "the repaint before the continuation to wait its turn, or to walk the tree beside the first");
        var continuedDuringTheWalk = app.Continued;
        app.ReleaseWalks();
        await dispatch;

        Assert.Equal(1, app.MostWalkingAtOnce);
        Assert.False(continuedDuringTheWalk);
        Assert.True(app.Continued);
    }
}

[SkipFactory]
internal sealed class HeldWalkApp : Component
{
    private readonly ManualResetEventSlim _walksMayFinish = new(initialState: true);
    private int _walking;
    private int _mostAtOnce;

    public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Continued { get; private set; }

    public int Walking => Volatile.Read(ref _walking);

    public int MostWalkingAtOnce => Volatile.Read(ref _mostAtOnce);

    public void HoldWalks() => _walksMayFinish.Reset();

    public void ReleaseWalks() => _walksMayFinish.Set();

    protected override string? HtmlLang => null;

    protected override Component? Render()
    {
        var now = Interlocked.Increment(ref _walking);
        InterlockedMax(ref _mostAtOnce, now);
        _walksMayFinish.Wait(WaitFor.HangCeiling);
        Interlocked.Decrement(ref _walking);
        return Button.OnClick(SaveThenContinue)["save"];
    }

    private async Task SaveThenContinue()
    {
        await Saved.Task;
        Continued = true;
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
