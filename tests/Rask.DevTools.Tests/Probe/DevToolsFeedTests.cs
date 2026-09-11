using Rask.DevTools.Probe;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     The per-session feed the panel reads: bounded, ordered, and pairing each render frame with the diff that
///     produced it.
/// </summary>
public sealed class DevToolsFeedTests
{
    [Fact]
    public void Events_come_back_oldest_first_with_rising_sequence_numbers()
    {
        var feed = new DevToolsFeed();

        feed.RecordWire(DevToolsWireDirection.Out, "event", 40, 1);
        feed.RecordWire(DevToolsWireDirection.In, "frame", 900, 2);
        feed.RecordWire(DevToolsWireDirection.In, "ack", 20, 3);

        var events = feed.WireSnapshot();

        Assert.Equal(["event", "frame", "ack"], events.Select(e => e.Kind));
        Assert.Equal([1L, 2L, 3L], events.Select(e => e.Sequence));
        Assert.Equal([40, 900, 20], events.Select(e => e.Bytes));
    }

    [Fact]
    public void A_full_feed_drops_its_oldest_events_first()
    {
        var feed = new DevToolsFeed();

        for (var i = 1; i <= DevToolsFeed.WireCapacity + 5; i++)
        {
            feed.RecordWire(DevToolsWireDirection.Out, "event", i, i);
        }

        var events = feed.WireSnapshot();

        Assert.Equal(DevToolsFeed.WireCapacity, events.Length);
        Assert.Equal(6, events[0].Bytes);
        Assert.Equal(DevToolsFeed.WireCapacity + 5, events[^1].Bytes);
        Assert.Equal(events[0].Sequence + DevToolsFeed.WireCapacity - 1, events[^1].Sequence);
    }

    [Fact]
    public void A_diff_is_attached_to_the_render_frame_that_carries_it_and_to_nothing_else()
    {
        var feed = new DevToolsFeed();

        feed.RecordDiff(opCount: 7, usedDiff: true);
        feed.RecordWire(DevToolsWireDirection.Out, "event", 40, 1);
        feed.RecordWire(DevToolsWireDirection.In, "frame", 300, 2);
        feed.RecordWire(DevToolsWireDirection.In, "frame", 500, 3);

        var events = feed.WireSnapshot();

        Assert.Null(events[0].DiffOps);
        Assert.Equal(7, events[1].DiffOps);
        // Consumed by the frame it described: the next render frame had no diff recorded for it.
        Assert.Null(events[2].DiffOps);
    }

    [Fact]
    public void A_full_document_render_carries_no_op_count()
    {
        var feed = new DevToolsFeed();

        feed.RecordDiff(opCount: 0, usedDiff: false);
        feed.RecordWire(DevToolsWireDirection.In, "frame", 12000, 1);

        Assert.Null(feed.WireSnapshot()[0].DiffOps);
    }

    [Fact]
    public void Every_recorded_event_notifies_once()
    {
        var feed = new DevToolsFeed();
        var notifications = 0;
        feed.Changed += () => notifications++;

        feed.RecordDiff(opCount: 3, usedDiff: true);
        feed.RecordWire(DevToolsWireDirection.In, "frame", 300, 1);
        feed.RecordWire(DevToolsWireDirection.Out, "event", 40, 2);

        // A diff alone changes nothing the panel lists; the two frames do.
        Assert.Equal(2, notifications);
    }
}
