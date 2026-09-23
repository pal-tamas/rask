using System.Text.Json;
using Rask.TestSupport;

namespace Rask.Core.Tests.Live;

/// <summary>
///     The runtime's loading mark: a control waiting on its own handler says so, and cannot be pressed twice.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-loading.ts</c> and <c>rask-morph.ts</c> in a Node subprocess against a stub
///     DOM with a fake clock (<c>LoadingStateFixture.ts</c>). The hosts' wiring — which press begins a ticket,
///     which ack ends it — is covered in a real browser by <c>ButtonLoadingTests</c>.
/// </remarks>
public sealed class LoadingStateTests
{
    private static JsonElement? Run() => NodeFixture.Run("LoadingStateFixture");

    [Fact]
    public void Buttons_and_opted_in_elements_wait_and_an_opt_out_anywhere_above_wins()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var target = root.GetProperty("target");
        Assert.True(target.GetProperty("button").GetBoolean());
        Assert.True(target.GetProperty("submitInput").GetBoolean());
        Assert.True(target.GetProperty("optedInDiv").GetBoolean());

        // A text field's change handler is not a press, and a plain div is not a control.
        Assert.False(target.GetProperty("textInput").GetBoolean());
        Assert.False(target.GetProperty("div").GetBoolean());

        // `data-rask-loading="off"` on the button, or on a whole toolbar of steppers around it.
        Assert.False(target.GetProperty("optedOutButton").GetBoolean());
        Assert.False(target.GetProperty("buttonUnderOptedOutAncestor").GetBoolean());
        Assert.False(target.GetProperty("none").GetBoolean());
    }

    [Fact]
    public void The_mark_waits_out_the_delay_and_comes_off_when_the_dispatch_ends()
    {
        if (Run() is not { } root)
        {
            return;
        }

        // Nothing inside the delay: a handler that answers in a frame never flashes a spinner.
        Assert.False(root.GetProperty("beforeDelay").GetProperty("loading").GetBoolean());
        Assert.False(root.GetProperty("beforeDelay").GetProperty("visibly").GetBoolean());
        Assert.True(root.GetProperty("fastNeverMarked").GetBoolean());

        var after = root.GetProperty("afterDelay");
        Assert.True(after.GetProperty("loading").GetBoolean());
        // aria-busy, never disabled: a disabled button throws keyboard focus off itself mid-press.
        Assert.Equal("true", after.GetProperty("busy").GetString());
        Assert.True(after.GetProperty("visibly").GetBoolean());
        Assert.True(after.GetProperty("owns").GetBoolean());
        Assert.False(after.GetProperty("ownsOther").GetBoolean());

        Assert.False(root.GetProperty("afterEnd").GetProperty("loading").GetBoolean());
        Assert.False(root.GetProperty("afterEnd").GetProperty("busy").GetBoolean());
    }

    [Fact]
    public void Two_presses_keep_the_mark_until_the_last_one_is_done()
    {
        if (Run() is not { } root)
        {
            return;
        }

        Assert.True(root.GetProperty("afterFirstOfTwo").GetBoolean(), "the first of two dispatches took the mark off.");
        Assert.False(root.GetProperty("afterSecondOfTwo").GetBoolean());
    }

    [Fact]
    public void A_mark_the_render_wrote_is_left_to_the_render()
    {
        if (Run() is not { } root)
        {
            return;
        }

        // Ui.Button.Loading(true) renders data-loading itself; a dispatch ending must not contradict it.
        Assert.True(root.GetProperty("serverOwned").GetBoolean());
        Assert.True(root.GetProperty("serverMarkKept").GetBoolean());
    }

    [Fact]
    public void A_dispatch_that_never_reports_back_and_a_dropped_connection_both_clear_the_mark()
    {
        if (Run() is not { } root)
        {
            return;
        }

        Assert.True(root.GetProperty("hardTimeoutCleared").GetBoolean());
        Assert.True(root.GetProperty("disconnectCleared").GetBoolean());
    }

    [Fact]
    public void The_morph_keeps_the_runtimes_mark_and_nothing_else_the_render_dropped()
    {
        if (Run() is not { } root)
        {
            return;
        }

        // The render the handler itself caused lands mid-dispatch and has no data-loading in it; stripping the
        // spinner there would end the wait on screen before the dispatch ended.
        Assert.True(root.GetProperty("morphKeptMark").GetBoolean());
        Assert.True(root.GetProperty("morphRemovedDropped").GetBoolean());
        Assert.True(root.GetProperty("afterEndMorphClean").GetBoolean());
        Assert.True(root.GetProperty("morphRemovedRenderedMark").GetBoolean());
    }
}
