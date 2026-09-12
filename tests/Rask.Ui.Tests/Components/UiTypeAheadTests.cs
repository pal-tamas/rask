namespace Rask.Ui.Tests.Components;

/// <summary>
///     Typing letters jumps to a node: the buffer, its timeout, and what a repeated letter means.
/// </summary>
public sealed class UiTypeAheadTests
{
    private static readonly string?[] Nodes = ["alpha", "beta", "album", "Beacon", "gamma", null];

    [Fact]
    public void A_letter_moves_to_the_next_match_after_the_cursor()
    {
        var clock = new ManualClock();
        var typeAhead = default(UiTypeAhead);

        Assert.Equal(2, typeAhead.Next("a", cursor: 0, Nodes, clock));
    }

    [Fact]
    public void The_search_wraps()
    {
        var clock = new ManualClock();
        var typeAhead = default(UiTypeAhead);

        Assert.Equal(0, typeAhead.Next("a", cursor: 4, Nodes, clock));
    }

    [Fact]
    public void A_repeated_letter_cycles_through_the_nodes_that_start_with_it()
    {
        var clock = new ManualClock();
        var typeAhead = default(UiTypeAhead);

        var first = typeAhead.Next("a", cursor: 0, Nodes, clock);
        var second = typeAhead.Next("a", first, Nodes, clock);

        // "aa" is somebody pressing the same key twice, not a node called "aa".
        Assert.Equal(2, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public void A_prefix_accumulates_within_the_timeout()
    {
        var clock = new ManualClock();
        var typeAhead = default(UiTypeAhead);

        typeAhead.Next("b", cursor: 0, Nodes, clock);
        clock.Advance(TimeSpan.FromMilliseconds(100));

        // "be" keeps the cursor's own row in the search, so refining a prefix cannot skip the node it just found.
        Assert.Equal(1, typeAhead.Next("e", cursor: 1, Nodes, clock));
    }

    [Fact]
    public void After_the_timeout_the_next_letter_starts_a_new_search()
    {
        var clock = new ManualClock();
        var typeAhead = default(UiTypeAhead);

        typeAhead.Next("b", cursor: 0, Nodes, clock);
        clock.Advance(UiTypeAhead.Timeout + TimeSpan.FromMilliseconds(1));

        // A fresh "e" matches nothing; had the buffer survived, "be" would have matched "beta".
        Assert.Equal(-1, typeAhead.Next("e", cursor: 1, Nodes, clock));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        var clock = new ManualClock();
        var typeAhead = default(UiTypeAhead);

        Assert.Equal(1, typeAhead.Next("B", cursor: 0, Nodes, clock));
    }

    [Fact]
    public void No_match_stays_where_it_is()
    {
        var clock = new ManualClock();
        var typeAhead = default(UiTypeAhead);

        Assert.Equal(-1, typeAhead.Next("z", cursor: 0, Nodes, clock));
    }

    [Fact]
    public void An_empty_tree_matches_nothing()
    {
        var clock = new ManualClock();
        var typeAhead = default(UiTypeAhead);

        Assert.Equal(-1, typeAhead.Next("a", cursor: 0, [], clock));
    }

    /// <summary>A clock the test moves itself, so the timeout is tested without waiting for it.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
