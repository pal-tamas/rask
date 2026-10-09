using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     The events one browser task produces leave as one frame, in the order they happened.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-batch.ts</c> — and <c>rask-input.ts</c> through it — in a Node subprocess
///     (<c>EventBatchFixture.ts</c>) against a host that batches the way both client runtimes do. Before this,
///     sixty charts measured by one <c>ResizeObserver</c> callback were sixty frames, each answered with a
///     render of the whole page.
/// </remarks>
public sealed class EventBatchTests
{
    private static JsonElement? Run() => NodeFixture.Run("EventBatchFixture");

    private static string[] Ids(JsonElement frame) =>
        [.. frame.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("id").GetString()!)];

    [Fact]
    public void Sixty_events_of_one_task_leave_as_one_frame_in_the_order_they_happened()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var frame = Assert.Single(root.GetProperty("sixty").EnumerateArray());

        Assert.Equal(0, root.GetProperty("duringTask").GetInt32());
        Assert.Equal("batch", frame.GetProperty("type").GetString());
        Assert.Equal(Enumerable.Range(0, 60).Select(i => $"h{i}"), Ids(frame));
    }

    [Fact]
    public void An_event_alone_in_its_task_travels_as_the_frame_it_always_was()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var frame = Assert.Single(root.GetProperty("alone").EnumerateArray());

        Assert.Equal("""{"id":"h1","type":"click"}""", frame.GetRawText());
    }

    [Fact]
    public void Events_of_two_tasks_are_two_frames()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var frames = root.GetProperty("twoTasks").EnumerateArray().Select(f => f.GetProperty("id").GetString()).ToArray();

        Assert.Equal(["h1", "h2"], frames);
    }

    [Fact]
    public void A_navigation_goes_at_once_behind_the_events_that_happened_before_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var atNavigation = root.GetProperty("atNavigation").EnumerateArray().ToArray();
        var afterNavigation = root.GetProperty("afterNavigation").EnumerateArray().ToArray();

        Assert.Equal(2, atNavigation.Length);
        Assert.Equal(["h1", "h2"], Ids(atNavigation[0]));
        Assert.Equal("navigate", atNavigation[1].GetProperty("type").GetString());
        Assert.Equal(3, afterNavigation.Length);
        Assert.Equal("h3", afterNavigation[2].GetProperty("id").GetString());
    }

    [Fact]
    public void More_events_than_a_frame_may_carry_go_in_a_frame_of_their_own()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var sizes = root.GetProperty("overflow").EnumerateArray().Select(n => n.GetInt32()).ToArray();

        Assert.Equal([256, 1], sizes);
    }

    [Fact]
    public void Every_event_of_a_batch_is_answered_when_its_frame_is_and_not_before()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var before = root.GetProperty("answeredBefore").GetInt32();
        var after = root.GetProperty("answeredAfter").GetInt32();

        Assert.Equal(0, before);
        Assert.Equal(3, after);
    }

    [Fact]
    public void Only_an_event_for_a_handler_is_held()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var kinds = root.GetProperty("kinds").EnumerateArray().Select(k => k.GetBoolean()).ToArray();

        Assert.Equal([true, false, false, false], kinds);
    }

    [Fact]
    public void Typed_text_flushed_ahead_of_a_click_leaves_in_the_same_frame_ahead_of_it()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var frame = Assert.Single(root.GetProperty("typedThenClicked").EnumerateArray());
        var events = frame.GetProperty("events").EnumerateArray().ToArray();

        Assert.Equal("input", events[0].GetProperty("type").GetString());
        Assert.Equal("Atlantis", events[0].GetProperty("value").GetString());
        Assert.Equal("click", events[1].GetProperty("type").GetString());
    }

    [Fact]
    public void Text_typed_while_a_frame_is_unanswered_is_held_until_that_frame_is_answered()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var whileOwed = root.GetProperty("heldWhileOwed").EnumerateArray().Select(f => f.GetProperty("value").GetString()).ToArray();
        var onceAnswered = root.GetProperty("sentOnceAnswered").EnumerateArray().Select(f => f.GetProperty("value").GetString()).ToArray();

        Assert.Equal(["A"], whileOwed);
        Assert.Equal(["A", "Atlantis"], onceAnswered);
    }
}
