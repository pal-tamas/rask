using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     Whether an action that asks first (<c>.Confirm</c>, Livewire's <c>wire:confirm</c>) may go: the browser's
///     confirm(), and prompt() matched exactly for the <c>expect:</c> form.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-confirm.ts</c> in a Node subprocess (<c>ConfirmFixture.ts</c>). That a refused
///     action sends nothing is <c>ConfirmTests</c> in <c>Rask.Server.E2E.Tests</c>, and on WebAssembly
///     <c>ConfirmWasmTests</c> in <c>Rask.Site.E2E.Tests</c>.
/// </remarks>
public sealed class ConfirmClientTests
{
    private static JsonElement? Run() => NodeFixture.Run("ConfirmFixture");

    [Fact]
    public void An_element_that_asks_nothing_goes_without_a_dialog()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var goes = root.GetProperty("plain").GetBoolean();
        var asked = root.GetProperty("askedOfPlain").GetInt32();

        Assert.True(goes);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void The_confirm_dialog_decides_whether_the_action_goes()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var accepted = root.GetProperty("accepted").GetBoolean();
        var dismissed = root.GetProperty("dismissed").GetBoolean();

        Assert.True(accepted);
        Assert.False(dismissed);
    }

    [Fact]
    public void A_yes_in_the_browsers_dialog_presses_the_button_again_once_the_held_page_caught_up()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var heldPressGoes = root.GetProperty("heldPressGoes").GetBoolean();
        var before = root.GetProperty("pressedBeforeTheTaskEnds").GetInt32();
        var after = root.GetProperty("pressedAgainAfterYes").GetInt32();

        Assert.False(heldPressGoes);
        Assert.Equal(0, before);
        Assert.Equal(1, after);
    }

    [Fact]
    public void The_prompt_goes_only_on_an_exact_case_sensitive_match()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var exact = root.GetProperty("typedExactly").GetBoolean();
        var otherCase = root.GetProperty("typedInAnotherCase").GetBoolean();
        var cancelled = root.GetProperty("promptCancelled").GetBoolean();

        Assert.True(exact);
        Assert.False(otherCase);
        Assert.False(cancelled);
    }

    [Fact]
    public void A_submit_asks_the_form_first_and_stops_at_the_first_no()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var formSaidNo = root.GetProperty("formSaidNo").GetBoolean();
        var askedWhenNo = root.GetProperty("askedWhenTheFormSaidNo").EnumerateArray().Select(e => e.GetString()).ToArray();
        var bothSaidYes = root.GetProperty("bothSaidYes").GetBoolean();
        var askedWhenYes = root.GetProperty("askedWhenBothSaidYes").EnumerateArray().Select(e => e.GetString()).ToArray();
        var noSubmitter = root.GetProperty("noSubmitter").GetBoolean();

        Assert.False(formSaidNo);
        Assert.Equal(["confirm:Send the order?"], askedWhenNo);
        Assert.True(bothSaidYes);
        Assert.Equal(["confirm:Send the order?", "confirm:Really?"], askedWhenYes);
        Assert.True(noSubmitter);
    }

    [Fact]
    public void With_the_kits_dialog_the_browser_is_not_asked_and_a_yes_makes_the_same_press_again()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var waits = root.GetProperty("pressWaitsForTheDialog").GetBoolean();
        var pressedAgain = root.GetProperty("pressedAgain").GetInt32();
        var inDialog = root.GetProperty("inDialog").EnumerateArray().Select(e => e.GetString()).ToArray();
        var browserAsked = root.GetProperty("browserAskedWithDialog").GetArrayLength();

        Assert.False(waits);
        Assert.Equal(1, pressedAgain);
        Assert.Equal(["Delete it?|Acme", "Send the order?|null"], inDialog);
        Assert.Equal(0, browserAsked);
    }

    [Fact]
    public void With_the_kits_dialog_a_yes_submits_the_form_again_by_the_same_button()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var waits = root.GetProperty("submitWaitsForTheDialog").GetBoolean();
        var sameButton = root.GetProperty("resubmittedBySameButton").GetBoolean();

        Assert.False(waits);
        Assert.True(sameButton);
    }

    [Fact]
    public void A_page_without_the_kits_dialog_asks_in_the_browser()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var goes = root.GetProperty("withoutDialogBrowserAsks").GetBoolean();
        var asked = root.GetProperty("browserAskedWithoutDialog").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.True(goes);
        Assert.Equal(["confirm:No dialog here?"], asked);
    }
}
