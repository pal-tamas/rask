using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     When the browser counts a form that asks before it is left (<c>ConfirmLeave</c>) as unsaved: from what it
///     heard typed, and from what the page says the reader chose in a control it draws (<c>data-rask-edited</c>).
/// </summary>
/// <remarks>
///     Drives the production <c>rask-leave.ts</c> in a Node subprocess (<c>ConfirmLeaveFixture.ts</c>). Each
///     answer is the message the guard would ask with, or null when there is nothing to lose. The journeys —
///     a link, Back, the dialog — are <c>ConfirmLeaveHookTests</c> in <c>Rask.Server.E2E.Tests</c>.
/// </remarks>
public sealed class ConfirmLeaveClientTests
{
    private const string Asks = "Leave the tank?";

    private static JsonElement? Run() => NodeFixture.Run("ConfirmLeaveFixture");

    private static string? Said(JsonElement root, string name) => root.GetProperty(name).GetString();

    [Fact]
    public void A_pick_the_page_heard_makes_the_form_unsaved_though_the_browser_heard_no_input()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var untouched = Said(root, "untouched");
        var afterAPick = Said(root, "afterAPick");

        Assert.Null(untouched);
        Assert.Equal(Asks, afterAPick);
    }

    [Fact]
    public void Closing_the_tab_asks_once_the_page_says_a_form_was_edited()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var asks = root.GetProperty("closingTheTabAsks").GetBoolean();

        Assert.True(asks);
    }

    [Fact]
    public void A_save_the_page_accepted_leaves_nothing_unsaved_and_a_pick_after_it_does()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var afterTheSave = Said(root, "afterTheSave");
        var typedAndPickedThenSaved = Said(root, "typedAndPickedThenSaved");
        var pickedAgain = Said(root, "pickedAgain");

        Assert.Null(afterTheSave);
        Assert.Null(typedAndPickedThenSaved);
        Assert.Equal(Asks, pickedAgain);
    }

    [Fact]
    public void Typing_still_counts_and_a_submit_the_page_refused_saves_nothing()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var afterTyping = Said(root, "afterTyping");
        var afterARefusedSubmit = Said(root, "afterARefusedSubmit");

        Assert.Equal(Asks, afterTyping);
        Assert.Equal(Asks, afterARefusedSubmit);
    }

    [Fact]
    public void What_is_typed_or_chosen_in_a_field_that_holds_none_of_the_forms_data_loses_nothing()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var afterSearching = Said(root, "afterSearching");
        var afterTheRuntimeWroteAField = Said(root, "afterTheRuntimeWroteAField");
        var afterTypingBesideTheSearch = Said(root, "afterTypingBesideTheSearch");

        Assert.Null(afterSearching);
        Assert.Null(afterTheRuntimeWroteAField);
        Assert.Equal(Asks, afterTypingBesideTheSearch);
    }

    [Fact]
    public void A_form_that_does_not_ask_is_never_unsaved()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var said = Said(root, "aFormThatDoesNotAsk");

        Assert.Null(said);
    }

    [Fact]
    public void Escape_and_a_press_outside_ask_before_a_dialog_holding_unsaved_edits_closes()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var untouched = root.GetProperty("escapeUntouchedRefused").GetBoolean();
        var inTheDialog = Said(root, "inTheDialog");
        var escape = root.GetProperty("escapeRefused").GetBoolean();
        var pressOutside = root.GetProperty("pressOutsideRefused").GetBoolean();
        var asked = root.GetProperty("askedWhileStaying").GetInt32();
        var stillOpen = root.GetProperty("stillOpen").GetBoolean();

        Assert.False(untouched);
        Assert.Equal(Asks, inTheDialog);
        Assert.True(escape);
        Assert.True(pressOutside);
        Assert.Equal(2, asked);
        Assert.True(stillOpen);
    }

    [Fact]
    public void A_dialog_the_reader_lets_go_closes_once_and_what_it_held_is_never_asked_about_again()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var closed = root.GetProperty("closedOnceLetGo").GetInt32();
        var afterLettingGo = Said(root, "afterLettingGo");
        var inAClosedDialog = Said(root, "inAClosedDialog");

        Assert.Equal(1, closed);
        Assert.Null(afterLettingGo);
        Assert.Null(inAClosedDialog);
    }
}
