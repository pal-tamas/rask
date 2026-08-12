namespace Rask.Sync.Tests;

// The clock is what every merge decision is made with, so its guarantees are asserted directly rather
// than inferred from the merge behaving well.
public class HlcTimestampTests
{
    [Fact]
    public void Orders_by_physical_time_first()
    {
        Assert.True(new HlcTimestamp(100, 9, "z") < new HlcTimestamp(101, 0, "a"));
    }

    [Fact]
    public void Breaks_a_tied_millisecond_with_the_counter()
    {
        Assert.True(new HlcTimestamp(100, 0, "z") < new HlcTimestamp(100, 1, "a"));
    }

    // Without this two devices can mint byte-identical stamps, and which edit survives comes down to which
    // op happened to be read first — a different answer per replica, which is divergence, not a merge.
    [Fact]
    public void Breaks_a_fully_tied_stamp_with_the_node_so_the_order_is_total()
    {
        Assert.True(new HlcTimestamp(100, 0, "node-a") < new HlcTimestamp(100, 0, "node-b"));
        Assert.NotEqual(0, new HlcTimestamp(100, 0, "node-a").CompareTo(new HlcTimestamp(100, 0, "node-b")));
    }

    // The wire form is fixed-width hex precisely so a log can be ordered by object key, with no parsing and
    // no index. If string order ever stopped matching value order, listing a prefix would silently replay
    // out of order.
    [Fact]
    public void Lexicographic_order_of_the_wire_form_matches_value_order()
    {
        var stamps = new List<HlcTimestamp>();
        for (var ms = 0L; ms < 40; ms++)
        {
            for (var counter = 0; counter < 4; counter++)
            {
                stamps.Add(new HlcTimestamp(ms * 7919, counter, counter % 2 == 0 ? "node-a" : "node-b"));
            }
        }

        var byValue = stamps.OrderBy(s => s).Select(s => s.ToString()).ToList();
        var byText = stamps.Select(s => s.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.Equal(byValue, byText);
    }

    [Fact]
    public void Round_trips_through_its_wire_form()
    {
        var stamp = new HlcTimestamp(1786000000000, 42, "9a3f1e7c-0000-4000-8000-000000000001");

        Assert.Equal(stamp, HlcTimestamp.Parse(stamp.ToString()));
    }

    // A node id is usually a Guid, which contains '-' — so parsing cannot split on separators.
    [Fact]
    public void Round_trips_a_node_id_containing_separators()
    {
        var stamp = new HlcTimestamp(5, 1, "a-b-c-d");

        Assert.Equal("a-b-c-d", HlcTimestamp.Parse(stamp.ToString()).NodeId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-stamp")]
    [InlineData("XXXXXXXXXXXX-0000-node")]
    [InlineData("00000000000A00000-node")]
    public void Rejects_malformed_text(string text)
    {
        Assert.False(HlcTimestamp.TryParse(text, out _));
    }
}

public class HybridLogicalClockTests
{
    private static readonly DateTimeOffset Base = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Advances_within_a_stalled_millisecond()
    {
        var clock = new HybridLogicalClock("node-a", new FakeTime(Base));

        var first = clock.Tick();
        var second = clock.Tick();

        Assert.True(second > first);
        Assert.Equal(first.PhysicalMs, second.PhysicalMs);
        Assert.Equal(first.Counter + 1, second.Counter);
    }

    // The failure this exists to prevent: a device whose clock is corrected backwards (NTP, a timezone
    // fix, a user setting it by hand) would otherwise issue stamps that sort BEFORE edits it already made,
    // so its own newer work would lose to its own older work and vanish with no error.
    [Fact]
    public void Never_goes_backwards_when_the_wall_clock_does()
    {
        var time = new FakeTime(Base);
        var clock = new HybridLogicalClock("node-a", time);

        var before = clock.Tick();
        time.Now = Base.AddHours(-3);
        var after = clock.Tick();

        Assert.True(after > before);
    }

    [Fact]
    public void Adopts_wall_time_when_it_moves_forward_and_resets_the_counter()
    {
        var time = new FakeTime(Base);
        var clock = new HybridLogicalClock("node-a", time);

        clock.Tick();
        clock.Tick();
        time.Now = Base.AddSeconds(1);
        var advanced = clock.Tick();

        Assert.Equal(Base.AddSeconds(1).ToUnixTimeMilliseconds(), advanced.PhysicalMs);
        Assert.Equal(0, advanced.Counter);
    }

    // The property that makes causality work across devices: anything issued after observing a remote
    // stamp sorts after it, so a reply always beats the thing it replied to regardless of whose clock is
    // wrong.
    [Fact]
    public void Anything_issued_after_observing_a_remote_stamp_sorts_after_it()
    {
        var clock = new HybridLogicalClock("node-a", new FakeTime(Base));
        var remote = new HlcTimestamp(Base.AddDays(2).ToUnixTimeMilliseconds(), 7, "node-b");

        var observed = clock.Observe(remote);
        var later = clock.Tick();

        Assert.True(observed > remote);
        Assert.True(later > observed);
    }

    [Fact]
    public void Observing_an_old_stamp_does_not_drag_the_clock_back()
    {
        var clock = new HybridLogicalClock("node-a", new FakeTime(Base));

        var local = clock.Tick();
        var observed = clock.Observe(new HlcTimestamp(1, 0, "node-b"));

        Assert.True(observed > local);
    }

    [Fact]
    public void Stamps_carry_the_node_identity()
    {
        Assert.Equal("node-a", new HybridLogicalClock("node-a", new FakeTime(Base)).Tick().NodeId);
    }

    [Fact]
    public void Rejects_a_blank_node_id()
    {
        Assert.ThrowsAny<ArgumentException>(() => new HybridLogicalClock("  "));
    }

    // Concurrency is real in a browser: a debounced flush and a user edit can both stamp at once. A
    // duplicate stamp would make two different edits indistinguishable and one of them unrecoverable.
    [Fact]
    public async Task Concurrent_ticks_never_produce_a_duplicate_stamp()
    {
        var clock = new HybridLogicalClock("node-a", new FakeTime(Base));
        var stamps = new System.Collections.Concurrent.ConcurrentBag<HlcTimestamp>();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++)
            {
                stamps.Add(clock.Tick());
            }
        })));

        Assert.Equal(2000, stamps.Distinct().Count());
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
