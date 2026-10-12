using System.Text.Json;

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

    [Fact]
    public void A_scope_is_the_nearest_element_above_the_press_that_says_within_unless_anything_says_off()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var scope = root.GetProperty("scopeOf");
        Assert.True(scope.GetProperty("descendant").GetBoolean());
        Assert.True(scope.GetProperty("itself").GetBoolean());
        Assert.True(scope.GetProperty("none").GetBoolean());
        Assert.True(scope.GetProperty("underOptOut").GetBoolean());
        Assert.True(scope.GetProperty("optOutBetween").GetBoolean());

        // A press that is neither a control's nor inside a scope opens nothing to end.
        Assert.True(scope.GetProperty("nothingToMark").GetBoolean());
    }

    [Fact]
    public void A_scope_is_marked_loading_after_the_delay_and_is_neither_busy_nor_unpressable()
    {
        if (Run() is not { } root)
        {
            return;
        }

        Assert.False(root.GetProperty("scopeBeforeDelay").GetBoolean());

        var after = root.GetProperty("scopeAfterDelay");
        Assert.True(after.GetProperty("loading").GetBoolean());
        Assert.True(after.GetProperty("control").GetBoolean(), "the pressed control lost its own mark.");

        // aria-busy stays on the control: a pager is not a control, and a region that says busy is read as one
        // whose content is changing, which the rows are and the pager is not.
        Assert.False(after.GetProperty("busy").GetBoolean());
        Assert.False(after.GetProperty("visibly").GetBoolean());
        Assert.True(after.GetProperty("owns").GetBoolean());
        Assert.False(after.GetProperty("ownsBusy").GetBoolean());
    }

    [Fact]
    public void A_scopes_mark_outlives_the_render_that_replaces_the_pressed_control_and_ends_with_the_dispatch()
    {
        if (Run() is not { } root)
        {
            return;
        }

        Assert.True(root.GetProperty("scopeMarkSurvivesTheRender").GetBoolean());
        Assert.False(root.GetProperty("scopeAfterEnd").GetBoolean());
    }

    [Fact]
    public void A_scope_waits_for_a_press_on_something_that_is_not_a_control_and_for_the_last_of_two()
    {
        if (Run() is not { } root)
        {
            return;
        }

        Assert.True(root.GetProperty("scopeMarkedByAPlainOwner").GetBoolean());
        Assert.True(root.GetProperty("scopeAfterFirstOfTwo").GetBoolean(), "the first of two dispatches took the mark off.");
        Assert.False(root.GetProperty("scopeAfterSecondOfTwo").GetBoolean());
        Assert.True(root.GetProperty("renderedScopeMarkKept").GetBoolean());
    }
}
