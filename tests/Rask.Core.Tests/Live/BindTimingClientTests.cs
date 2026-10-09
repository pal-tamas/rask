using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     What the browser sends for a bound field that waits — <c>.Debounce(…)</c> and <c>.Blur()</c> — and when.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-input.ts</c> in a Node subprocess with a clock the fixture moves
///     (<c>BindTimingFixture.ts</c>). Each value sent is written <c>type:value</c>, and a message with no
///     value <c>type:handler</c>.
/// </remarks>
public sealed class BindTimingClientTests
{
    private static JsonElement? Run() => NodeFixture.Run("BindTimingFixture");

    private static string[] Sent(JsonElement root, string name) =>
        [.. root.GetProperty(name).EnumerateArray().Select(value => value.GetString()!)];

    [Fact]
    public void A_debounced_field_says_nothing_while_typed_into_and_sends_one_value_at_the_pause()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var beforePause = Sent(root, "beforePause");
        var atPause = Sent(root, "atPause");
        var afterPause = Sent(root, "afterPause");

        Assert.Empty(beforePause);
        Assert.Equal(["input:Atl"], atPause);
        Assert.Equal(["input:Atl"], afterPause);
    }

    [Fact]
    public void What_a_pause_still_holds_is_sent_ahead_of_the_event_that_follows_and_only_once()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var flushed = Sent(root, "flushed");
        var flushedOnce = Sent(root, "flushedOnce");

        Assert.Equal(["input:At"], flushed);
        Assert.Equal(["input:At"], flushedOnce);
    }

    [Fact]
    public void Leaving_a_debounced_field_sends_what_its_pause_was_holding()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var left = Sent(root, "left");

        Assert.Equal(["input:Atl"], left);
    }

    [Fact]
    public void No_pause_is_counted_while_a_character_is_being_composed()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var composing = Sent(root, "composing");
        var composedTooSoon = Sent(root, "composedTooSoon");
        var composed = Sent(root, "composed");

        Assert.Empty(composing);
        Assert.Empty(composedTooSoon);
        Assert.Equal(["input:あ"], composed);
    }

    [Fact]
    public void A_pause_that_ends_while_the_last_value_is_unanswered_is_held_and_sent_with_the_answer()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var heldAtPause = Sent(root, "heldAtPause");
        var heldSent = Sent(root, "heldSent");

        Assert.Equal(["input:A"], heldAtPause);
        Assert.Equal(["input:A", "input:At"], heldSent);
    }

    [Fact]
    public void A_debounced_field_emptied_by_a_key_sends_what_it_says_now()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var cleared = Sent(root, "cleared");

        Assert.Equal(["input:"], cleared);
    }

    [Fact]
    public void A_field_showing_a_message_reports_its_first_keystroke_once()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var edited = Sent(root, "edited");
        var stillAsked = root.GetProperty("editStillAsked").GetBoolean();

        Assert.Equal(["edit:h9"], edited);
        Assert.False(stillAsked);
    }

    [Fact]
    public void A_field_bound_on_blur_says_nothing_while_typed_into_and_is_sent_once_ahead_of_what_follows()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var typed = Sent(root, "typedIntoBlur");
        var committed = Sent(root, "committedOnce");

        Assert.Empty(typed);
        Assert.Equal(["change:At"], committed);
    }

    [Fact]
    public void The_change_the_browser_fires_for_a_value_already_sent_is_not_sent_again()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var duplicate = root.GetProperty("duplicate").GetBoolean();
        var askedAgain = root.GetProperty("askedAgain").GetBoolean();
        var changedSince = root.GetProperty("changedSince").GetBoolean();

        Assert.True(duplicate);
        Assert.False(askedAgain);
        Assert.False(changedSince);
    }

    [Fact]
    public void A_change_the_browser_fires_first_is_the_hosts_to_send_and_leaves_nothing_to_flush()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var ownedByHost = root.GetProperty("ownedByHost").GetBoolean();
        var afterNativeChange = Sent(root, "afterNativeChange");

        Assert.False(ownedByHost);
        Assert.Empty(afterNativeChange);
    }
}
