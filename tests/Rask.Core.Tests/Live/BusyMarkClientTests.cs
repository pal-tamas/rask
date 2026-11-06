using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     When <c>&lt;html&gt;</c> says <c>data-rask-busy</c>: from the first hold, in the same task, until nothing holds,
///     and never idle in between when one hold hands over to the next.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-busy.ts</c> and <c>rask-owned.ts</c> in-process on Jint (<c>BusyFixture.ts</c>).
///     The journeys on a real page are <c>WaitForRaskTests</c> in <c>Rask.Server.E2E.Tests</c> and
///     <c>WaitForRaskWasmTests</c> in <c>Rask.Site.E2E.Tests</c>.
/// </remarks>
public sealed class BusyMarkClientTests
{
    private static JsonElement Run() => ScriptFixture.Run("BusyFixture");

    private static bool Read(JsonElement root, string name) => root.GetProperty(name).GetBoolean();

    [Fact]
    public void The_mark_goes_on_in_the_task_that_holds_and_comes_off_once_nothing_holds()
    {
        var root = Run();

        var idle = Read(root, "idleAtFirst");
        var marked = Read(root, "markedInTheSameTask");
        var letGo = Read(root, "stillMarkedInTheTaskThatLetGo");
        var cleared = Read(root, "clearedOnceNothingHolds");

        Assert.False(idle);
        Assert.True(marked);
        Assert.True(letGo);
        Assert.False(cleared);
    }

    [Fact]
    public void The_page_stays_busy_while_any_hold_remains_and_across_a_hand_over_in_one_task()
    {
        var root = Run();

        var secondHolds = Read(root, "markedWhileTheSecondHolds");
        var bothGone = Read(root, "clearedWhenBothLetGo");
        var handOver = Read(root, "neverIdleAcrossAHandOver");
        var secondRelease = Read(root, "aSecondReleaseLetsNothingElseGo");

        Assert.True(secondHolds);
        Assert.False(bothGone);
        Assert.True(handOver);
        Assert.True(secondRelease);
    }

    [Fact]
    public void A_pending_frame_typing_and_an_unanswered_event_each_hold_the_page_until_they_are_done()
    {
        var root = Run();

        var frame = Read(root, "markedWhileTheFrameIsPending");
        var applied = Read(root, "clearedWhenTheFrameApplied");
        var typing = Read(root, "markedWhileTyping");
        var typed = Read(root, "clearedWhenTypingStops");
        var awaiting = Read(root, "markedWhileAwaitingAnAnswer");
        var answered = Read(root, "clearedWhenAnswered");

        Assert.True(frame);
        Assert.False(applied);
        Assert.True(typing);
        Assert.False(typed);
        Assert.True(awaiting);
        Assert.False(answered);
    }

    [Fact]
    public void A_morph_leaves_the_mark_on_html_and_only_there()
    {
        var root = Run();

        var owns = Read(root, "ownsItOnHtml");
        var others = Read(root, "ownsNothingElse");

        Assert.True(owns);
        Assert.False(others);
    }
}
