using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     What is typed into a field while .NET still owes it an answer is held, and only the latest value goes.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-input.ts</c> in a Node subprocess against a host that answers when the
///     fixture says so (<c>InputHoldFixture.ts</c>) — the slow path a browser reaches only under load. Before
///     this, eight letters typed at once into a page that renders slower than a key were eight renders queued
///     behind each other: the published site's autocomplete answered a Tab seconds later, and its end-to-end
///     test timed out on a loaded runner.
/// </remarks>
public sealed class InputHoldTests
{
    private static JsonElement? Run() => NodeFixture.Run("InputHoldFixture");

    private static string[] Sent(JsonElement root, string name) =>
        [.. root.GetProperty(name).EnumerateArray().Select(value => value.GetString()!)];

    [Fact]
    public void Letters_typed_before_the_first_is_answered_go_as_one_value_when_it_is()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var whileOwed = Sent(root, "whileOwed");
        var afterAnswer = Sent(root, "afterAnswer");
        var afterAllAnswered = Sent(root, "afterAllAnswered");

        Assert.Equal(["A"], whileOwed);
        Assert.Equal(["A", "Atlantis"], afterAnswer);
        Assert.Equal(["A", "Atlantis", "Atlantis!"], afterAllAnswered);
    }

    [Fact]
    public void What_is_held_is_sent_at_once_ahead_of_the_event_that_follows_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var flushed = Sent(root, "flushed");
        var oneOfTwoAnswered = Sent(root, "oneOfTwoAnswered");
        var bothAnswered = Sent(root, "bothAnswered");

        Assert.Equal(["A", "At"], flushed);
        // Two values are owed an answer now, and the field is held until the second one has it.
        Assert.Equal(["A", "At"], oneOfTwoAnswered);
        Assert.Equal(["A", "At", "Atl"], bothAnswered);
    }

    [Fact]
    public void A_field_that_is_gone_when_its_answer_comes_sends_nothing_more()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var gone = Sent(root, "gone");

        Assert.Equal(["A"], gone);
    }

    // A field with no change handler says nothing when it is left, so the render that answers its first
    // letter is written into it while the rest is still held. Read back then, the rest would be lost.
    [Fact]
    public void What_was_typed_is_sent_even_when_a_late_render_has_written_over_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "overwritten");

        Assert.Equal(["A", "Atlantis"], sent);
    }

    [Fact]
    public void A_field_sent_once_a_frame_is_held_the_same_way()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var beforeFrame = Sent(root, "beforeFrame");
        var firstFrame = Sent(root, "firstFrame");
        var heldAcrossFrame = Sent(root, "heldAcrossFrame");
        var frameAnswered = Sent(root, "frameAnswered");

        Assert.Empty(beforeFrame);
        Assert.Equal(["Te"], firstFrame);
        Assert.Equal(["Te"], heldAcrossFrame);
        Assert.Equal(["Te", "Tex"], frameAnswered);
    }

    [Fact]
    public void A_host_that_does_not_say_when_it_has_answered_is_sent_every_value()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sent = Sent(root, "unansweredHost");

        Assert.Equal(["A", "At", "Atl"], sent);
    }
}
