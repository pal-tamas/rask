using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     What a Server tab sends again on its next connection: every event the last one carried that no ack answered,
///     in the order it left — driven in a Node subprocess (<c>ResendFixture.ts</c>).
/// </summary>
public sealed class ResendFixtureTests
{
    private static JsonElement? Run() => NodeFixture.Run("ResendFixture");

    [Fact]
    public void Events_no_ack_answered_go_again_in_the_order_they_left()
    {
        // No node on PATH — the fixture cannot run. Not a failure: the Server E2E suite drives the same in a browser.
        if (Run() is not { } result)
        {
            return;
        }

        var afterAck = result.GetProperty("afterAck");

        Assert.Equal(["c2", "c3"], Strings(afterAck.GetProperty("again")));
        Assert.Equal(3, afterAck.GetProperty("newest").GetInt32());
    }

    [Fact]
    public void An_ack_answers_every_event_up_to_its_own_and_a_frame_with_no_event_is_never_held()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var answered = result.GetProperty("allAnswered");

        Assert.Empty(Strings(answered.GetProperty("again")));
        Assert.Equal(2, answered.GetProperty("newest").GetInt32());
    }

    [Fact]
    public void A_sign_in_lets_go_of_what_is_held_but_the_next_hello_still_names_the_newest_seq()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var forgotten = result.GetProperty("forgotten");

        Assert.Empty(Strings(forgotten.GetProperty("again")));
        Assert.Equal(1, forgotten.GetProperty("newest").GetInt32());
    }

    [Fact]
    public void What_is_held_is_capped_and_the_oldest_goes_first()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var capped = result.GetProperty("capped");

        Assert.Equal(capped.GetProperty("max").GetInt32(), capped.GetProperty("count").GetInt32());
        Assert.Equal("c3", capped.GetProperty("first").GetString());
    }

    [Fact]
    public void The_newest_seq_of_a_frame_is_its_own_or_the_newest_in_its_batch_and_zero_without_one()
    {
        if (Run() is not { } result)
        {
            return;
        }

        var seqs = result.GetProperty("seqs").EnumerateArray().Select(e => e.GetInt32()).ToArray();

        Assert.Equal([4, 7, 0, 0], seqs);
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();
}
