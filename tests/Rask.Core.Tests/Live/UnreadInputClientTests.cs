using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     What the browser sends for a number, date or time input holding text it will not hand over, and what
///     becomes of a submit the browser refuses over one.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-input.ts</c> in a Node subprocess (<c>UnreadInputFixture.ts</c>). Each frame
///     is written <c>type:value</c>, with <c>!kind</c> after it when it says the field held text back. A missing
///     Node returns early: the browser half is <c>NumberInputReadTests</c>.
/// </remarks>
public sealed class UnreadInputClientTests
{
    private static JsonElement? Run() => NodeFixture.Run("UnreadInputFixture");

    private static string[] Sent(JsonElement root, string name) =>
        [.. root.GetProperty(name).EnumerateArray().Select(value => value.GetString()!)];

    [Fact]
    public void A_number_field_holding_text_back_says_so_in_the_frame_the_next_action_carries()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "heldBackBeforeAClick");

        Assert.Equal(["change:!number", "click:save"], sent);
    }

    [Fact]
    public void A_number_that_reads_and_a_text_field_say_nothing_of_the_kind()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "readableSaysNothing");

        Assert.Equal(["change:12", "change:", "click:save"], sent);
    }

    [Fact]
    public void A_date_and_a_time_half_typed_name_their_own_kind()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "aDateAndATime");

        Assert.Equal(["change:!date", "change:!time", "click:save"], sent);
    }

    [Fact]
    public void A_live_field_says_so_when_its_pause_sends_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "liveAtItsPause");

        Assert.Equal(["input:!number"], sent);
    }

    [Fact]
    public void A_field_bound_on_blur_is_sent_once_as_it_is_left_though_the_browser_fires_no_change()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var beforeLeaving = Sent(root, "beforeLeaving");
        var onLeaving = Sent(root, "onLeaving");
        var afterLeaving = Sent(root, "afterLeaving");
        var leavingAReadableField = Sent(root, "leavingAReadableField");

        Assert.Empty(beforeLeaving);
        Assert.Equal(["change:!number"], onLeaving);
        Assert.Equal(["click:save"], afterLeaving);
        Assert.Empty(leavingAReadableField);
    }

    [Fact]
    public void A_submit_the_browser_refuses_over_an_unread_field_goes_anyway_with_the_field_ahead_of_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var bubbleCalledOff = root.GetProperty("bubbleCalledOff").GetBoolean();
        var beforeItsTask = Sent(root, "beforeItsTask");
        var submittedAnyway = Sent(root, "submittedAnyway");

        Assert.True(bubbleCalledOff);
        Assert.Empty(beforeItsTask);
        Assert.Equal(["change:!number", "submit:form"], submittedAnyway);
    }

    [Fact]
    public void A_fraction_in_a_field_with_no_step_of_its_own_is_submitted_with_its_text()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var calledOff = root.GetProperty("fractionCalledOff").GetBoolean();
        var sent = Sent(root, "fractionSubmitted");

        Assert.True(calledOff);
        Assert.Equal(["change:1.5", "submit:form"], sent);
    }

    [Fact]
    public void A_date_half_typed_that_fired_no_input_is_still_sent_ahead_of_the_submit()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "aDateNobodyHeardTyped");

        Assert.Equal(["change:!date", "submit:form"], sent);
    }

    [Fact]
    public void Two_unread_fields_of_one_form_are_one_submit()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var submits = root.GetProperty("twoFieldsOneSubmit").GetInt32();

        Assert.Equal(1, submits);
    }

    [Fact]
    public void Beside_a_field_the_browser_refuses_for_its_own_reason_only_the_unread_field_is_sent()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var calledOff = root.GetProperty("besideRequiredCalledOff").GetBoolean();
        var leftToTheBrowser = root.GetProperty("requiredLeftToTheBrowser").GetBoolean();
        var sent = Sent(root, "besideRequired");
        var submits = root.GetProperty("besideRequiredSubmits").GetInt32();

        Assert.True(calledOff);
        Assert.True(leftToTheBrowser);
        Assert.Equal(["change:!number"], sent);
        Assert.Equal(0, submits);
    }

    [Fact]
    public void A_step_the_app_wrote_an_unbound_field_and_a_number_out_of_its_range_stay_the_browsers()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var calledOff = root.GetProperty("leftToTheBrowser").EnumerateArray().Select(value => value.GetBoolean()).ToArray();
        var submits = root.GetProperty("leftToTheBrowserSubmits").GetInt32();

        Assert.Equal([false, false, false], calledOff);
        Assert.Equal(0, submits);
    }
}
