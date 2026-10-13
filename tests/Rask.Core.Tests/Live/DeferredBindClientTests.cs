using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     What the browser sends for a bound control that waits for the next action — what a plain
///     <c>.Bind(…)</c> renders — and in which order, for every kind of event that counts as one.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-input.ts</c> and <c>rask-events.ts</c> in a Node subprocess
///     (<c>DeferredBindFixture.ts</c>) behind a host that, like both real ones, asks for what waits to be sent
///     first. Each frame is written <c>type:value</c>, and one with no value <c>type:handler</c>.
/// </remarks>
public sealed class DeferredBindClientTests
{
    private static JsonElement? Run() => NodeFixture.Run("DeferredBindFixture");

    private static string[] Sent(JsonElement root, string name) =>
        [.. root.GetProperty(name).EnumerateArray().Select(value => value.GetString()!)];

    [Fact]
    public void A_field_that_waits_sends_nothing_while_typed_into_paused_over_or_left()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var typedAndLeft = Sent(root, "typedAndLeft");
        var hostSkipsItsChange = root.GetProperty("hostSkipsItsChange").GetBoolean();

        Assert.Empty(typedAndLeft);
        Assert.True(hostSkipsItsChange);
    }

    [Fact]
    public void A_click_is_preceded_by_what_was_typed_and_the_value_is_sent_once()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var beforeAClick = Sent(root, "beforeAClick");
        var clickedAgain = Sent(root, "clickedAgain");
        var browsersChangeAfterwards = root.GetProperty("browsersChangeAfterwards").GetBoolean();

        Assert.Equal(["change:At", "click:save"], beforeAClick);
        Assert.Equal(["click:save"], clickedAgain);
        Assert.True(browsersChangeAfterwards);
    }

    [Fact]
    public void A_submit_and_a_navigation_are_preceded_by_what_was_typed()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var beforeASubmit = Sent(root, "beforeASubmit");
        var beforeANavigation = Sent(root, "beforeANavigation");

        Assert.Equal(["change:Bea", "submit:form"], beforeASubmit);
        Assert.Equal(["change:Cy", "navigate:"], beforeANavigation);
    }

    [Fact]
    public void Several_fields_are_sent_in_the_order_they_were_last_touched()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "inTheOrderLastTouched");

        Assert.Equal(["change:2", "change:3", "change:1!", "click:save"], sent);
    }

    [Fact]
    public void A_key_a_handler_hears_is_preceded_by_what_was_typed_and_a_key_it_does_not_hear_sends_nothing()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var aKeyNobodyHears = Sent(root, "aKeyNobodyHears");
        var beforeAHandledKey = Sent(root, "beforeAHandledKey");

        Assert.Empty(aKeyNobodyHears);
        Assert.Equal(["change:Dee", "keydown:keys"], beforeAHandledKey);
    }

    [Fact]
    public void A_key_heard_only_from_an_empty_field_is_not_sent_while_the_field_holds_text()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var overText = Sent(root, "backspaceOverText");
        var inAnEmptyField = Sent(root, "backspaceInAnEmptyField");

        Assert.Empty(overText);
        Assert.Equal(["keydown:pills"], inAnEmptyField);
    }

    [Theory]
    [InlineData("beforeATableEvent", "change:Eve", "dblclick:twice")]
    [InlineData("beforeAPointerEntering", "change:Fay", "mouseenter:over")]
    [InlineData("beforeADrag", "change:Gus", "dragstart:drag")]
    [InlineData("beforeAToggle", "change:Ida", "toggle:opened")]
    [InlineData("aReplayedPress", "change:Ian", "click:row")]
    public void Every_other_event_the_reader_raises_is_preceded_by_what_was_typed(string family, string value, string after)
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, family);

        Assert.Equal([value, after], sent);
    }

    [Fact]
    public void A_scroll_waits_for_its_frame_and_takes_nothing_with_it_before_then()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var scrollBeforeItsFrame = Sent(root, "scrollBeforeItsFrame");

        Assert.Empty(scrollBeforeItsFrame);
    }

    [Fact]
    public void A_live_field_sends_once_after_150_milliseconds_and_carries_what_was_typed_before_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var liveBeforeItsPause = Sent(root, "liveBeforeItsPause");
        var beforeALiveFieldsPause = Sent(root, "beforeALiveFieldsPause");
        var liveSaysNoMore = Sent(root, "liveSaysNoMore");

        Assert.Empty(liveBeforeItsPause);
        Assert.Equal(["change:Jo", "input:x"], beforeALiveFieldsPause);
        Assert.Empty(liveSaysNoMore);
    }

    [Fact]
    public void An_event_a_control_hears_only_to_draw_itself_is_sent_alone_and_marked_and_carries_nothing()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var whatOnlyDraws = Sent(root, "whatOnlyDraws");

        Assert.Equal(
            ["toggle:list~", "mouseenter:row~", "keydown:keys~", "input:Ad~", "input:3~", "click:pick~", "change:month~"],
            whatOnlyDraws);
    }

    [Fact]
    public void A_handler_the_element_does_not_name_as_drawing_is_an_action_and_what_waited_goes_ahead_of_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var aHandlerNotNamed = Sent(root, "aHandlerNotNamed");

        Assert.Equal(["change:Liv", "toggle:opened"], aHandlerNotNamed);
    }

    [Fact]
    public void What_waited_while_a_control_drew_itself_goes_with_the_next_action_ahead_of_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var afterTheDrawing = Sent(root, "afterTheDrawing");

        Assert.Equal(["toggle:list~", "change:Liv!", "input:AdB", "click:save"], afterTheDrawing);
    }

    [Fact]
    public void A_field_sent_at_every_key_goes_at_once_behind_what_was_typed_before_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var atEveryKey = Sent(root, "beforeAFieldSentAtEveryKey");
        var onceAFrame = Sent(root, "beforeAFieldSentOnceAFrame");

        Assert.Equal(["change:Kay", "input:y"], atEveryKey);
        Assert.Equal(["change:Lee", "input:z"], onceAFrame);
    }

    [Fact]
    public void A_checkbox_and_a_select_that_wait_say_nothing_when_chosen_and_go_ahead_of_the_click()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var chosenAndLeft = Sent(root, "chosenAndLeft");
        var choicesBeforeAClick = Sent(root, "choicesBeforeAClick");

        Assert.Empty(chosenAndLeft);
        Assert.Equal(["change:true", "change:pro", "click:save"], choicesBeforeAClick);
    }

    [Fact]
    public void Of_a_radio_group_that_waits_only_the_one_left_checked_is_sent()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "theRadioChecked");

        Assert.Equal(["change:cash", "click:save"], sent);
    }

    [Fact]
    public void The_first_keystroke_of_a_correction_is_reported_alone_and_the_correction_waits_for_the_action()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var firstKeystroke = Sent(root, "firstKeystrokeOfACorrection");
        var thenTheCorrection = Sent(root, "thenTheCorrection");

        Assert.Equal(["edit:stale"], firstKeystroke);
        Assert.Equal(["change:Mo", "click:save"], thenTheCorrection);
    }

    [Theory]
    [InlineData("aToastsOwnPress", "click:close~")]
    [InlineData("aMeasuredBox", "input:640x480~")]
    [InlineData("aScroll", "scroll:scrolled~")]
    [InlineData("mediaPlayingOn", "timeupdate:playing~")]
    [InlineData("anAnimationEnding", "animationend:faded~")]
    [InlineData("anImageArriving", "load:arrived~")]
    [InlineData("anInteropReply", "jsResult:7")]
    [InlineData("aFieldARenderFocused", "focus:arrived~")]
    public void What_the_runtime_does_by_itself_is_sent_alone_and_marked_with_nothing_typed_ahead_of_it(string doing, string alone)
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, doing);

        Assert.Equal([alone], sent);
    }

    [Fact]
    public void What_waited_through_everything_the_runtime_did_by_itself_goes_with_the_readers_next_action()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "stillSentWithTheAction");

        Assert.Equal(["change:Hal", "click:save"], sent);
    }

    [Fact]
    public void A_field_that_left_the_page_before_the_action_is_not_sent()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "goneBeforeTheAction");

        Assert.Equal(["click:save"], sent);
    }

    [Fact]
    public void A_field_filled_in_again_after_a_reload_waits_as_it_did_before()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var restored = Sent(root, "restored");
        var restoredBeforeAClick = Sent(root, "restoredBeforeAClick");

        Assert.Empty(restored);
        Assert.Equal(["change:Pat", "click:save"], restoredBeforeAClick);
    }

    [Fact]
    public void A_live_field_counts_no_pause_while_a_character_is_being_composed()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var whileComposing = Sent(root, "whileComposing");
        var afterComposing = Sent(root, "afterComposing");

        Assert.Empty(whileComposing);
        Assert.Equal(["input:あ"], afterComposing);
    }

    [Fact]
    public void What_a_host_still_owes_an_answer_for_is_held_and_goes_ahead_of_the_click_once()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var heldWhileOwed = Sent(root, "heldWhileOwed");
        var heldBeforeAClick = Sent(root, "heldBeforeAClick");
        var afterTheAnswers = Sent(root, "afterTheAnswers");

        Assert.Equal(["input:a"], heldWhileOwed);
        Assert.Equal(["change:Quy", "input:ab", "click:save"], heldBeforeAClick);
        Assert.Empty(afterTheAnswers);
    }
}
