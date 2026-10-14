using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     A form that is submitting takes no second submit: Livewire's <c>wire:submit</c> rule.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-submitting.ts</c> and <c>rask-morph.ts</c> in a Node subprocess against a
///     stub DOM with a fake clock (<c>FormSubmittingFixture.ts</c>). The hosts' wiring — the press holds, the ack
///     releases — is covered in a real browser by <c>FormSubmitInFlightTests</c>.
/// </remarks>
public sealed class FormSubmittingTests
{
    private static JsonElement? Run() => NodeFixture.Run("FormSubmittingFixture");

    [Fact]
    public void At_the_press_the_submit_buttons_are_disabled_and_the_fields_readonly()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var atPress = root.GetProperty("atPress");

        Assert.False(root.GetProperty("before").GetBoolean());
        Assert.True(atPress.GetProperty("submitting").GetBoolean());
        Assert.True(atPress.GetProperty("submitButtonsDisabled").GetBoolean());
        Assert.True(atPress.GetProperty("fieldsReadonly").GetBoolean());
        Assert.True(atPress.GetProperty("plainButtonLive").GetBoolean());
        Assert.True(atPress.GetProperty("restUntouched").GetBoolean());
        Assert.True(atPress.GetProperty("owned").GetBoolean());
    }

    [Fact]
    public void A_second_submit_while_the_first_is_in_flight_is_refused()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var refused = root.GetProperty("secondRefused").GetBoolean();
        var next = root.GetProperty("takesTheNextSubmit").GetBoolean();

        Assert.True(refused);
        Assert.True(next, "a released form refused the next submit.");
    }

    [Fact]
    public void A_render_mid_flight_keeps_the_hold_and_its_word_is_what_the_control_gets_after()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var midFlight = root.GetProperty("midFlight");
        var after = root.GetProperty("afterRelease");

        Assert.True(midFlight.GetProperty("stillDisabled").GetBoolean());
        Assert.True(midFlight.GetProperty("restRendered").GetBoolean());
        Assert.False(after.GetProperty("submitting").GetBoolean());
        Assert.True(after.GetProperty("buttonsBack").GetBoolean());
        Assert.True(after.GetProperty("renderedDisabledKept").GetBoolean());
        Assert.True(after.GetProperty("fieldsBack").GetBoolean());
        Assert.True(after.GetProperty("disowned").GetBoolean());
    }

    [Fact]
    public void What_the_render_wrote_before_the_press_stays_the_renders()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var kept = root.GetProperty("renderedKept").GetBoolean();

        Assert.True(kept);
    }

    [Fact]
    public void A_submit_that_never_reports_back_and_a_dropped_connection_both_release_the_form()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var held = root.GetProperty("hungHeld").GetBoolean();
        var timedOut = root.GetProperty("hardTimeoutReleased").GetBoolean();
        var dropped = root.GetProperty("disconnectReleased").GetBoolean();

        Assert.True(held);
        Assert.True(timedOut);
        Assert.True(dropped);
    }
}
