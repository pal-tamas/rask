using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace Rask.Wire.Tests;

/// <summary>The keys Rask makes before the insert: a typed id's <c>New()</c>, a battery's rows, a plain Guid key.</summary>
public sealed class OrderedGuidTests
{
    [Fact]
    public void A_new_key_is_a_version_7_Guid_later_than_every_one_made_before_it()
    {
        var made = new List<Guid>();

        for (var i = 0; i < 10_000; i++)
        {
            made.Add(OrderedGuid.New());
        }

        Assert.All(made, static id => Assert.Equal(7, id.Version));
        Assert.Equal(made.Order(), made);
        Assert.Equal(made.Count, made.Distinct().Count());
    }

    [Fact]
    public void Keys_made_on_many_threads_at_once_never_meet_and_each_thread_s_run_increases()
    {
        var runs = new ConcurrentBag<List<Guid>>();

        Parallel.For(0, Environment.ProcessorCount * 2, _ =>
        {
            var run = new List<Guid>(2_000);
            for (var i = 0; i < 2_000; i++)
            {
                run.Add(OrderedGuid.New());
            }

            runs.Add(run);
        });

        var all = runs.SelectMany(static r => r).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(runs, static run => Assert.Equal(run.Order(), run));
        Assert.Contains(all.GroupBy(Millisecond), static g => g.Count() > 1);
    }

    [Fact]
    public void Keys_sort_as_PostgreSQL_s_uuid_and_SQLite_s_text_sort_them()
    {
        var made = Enumerable.Range(0, 5_000).Select(static _ => OrderedGuid.New()).ToList();
        var shuffled = made.OrderBy(static _ => Random.Shared.Next()).ToList();

        var byBytes = shuffled.OrderBy(static id => id.ToByteArray(bigEndian: true), ByteOrder.Instance).ToList();
        var byText = shuffled.OrderBy(static id => id.ToString(), StringComparer.Ordinal).ToList();

        Assert.Equal(made, byBytes);
        Assert.Equal(made, byText);
    }

    [Fact]
    public void A_fake_time_source_stamps_its_own_time_and_counts_within_its_millisecond()
    {
        var at = new DateTimeOffset(2001, 2, 3, 4, 5, 6, 789, TimeSpan.Zero);
        var time = new FrozenTime(at);

        var made = Enumerable.Range(0, 4_097).Select(_ => OrderedGuid.New(time)).ToList();

        Assert.Equal(made.Order(), made);
        Assert.All(made.Take(4_096), id => Assert.Equal(at.ToUnixTimeMilliseconds(), Millisecond(id)));
        Assert.Equal(Enumerable.Range(0, 4_096), made.Take(4_096).Select(static id => (int)Counter(id)));
        Assert.Equal(at.ToUnixTimeMilliseconds() + 1, Millisecond(made[^1]));
        Assert.Equal(0, Counter(made[^1]));
    }

    [Fact]
    public void Two_fake_time_sources_count_apart_so_neither_runs_the_other_ahead()
    {
        var at = new DateTimeOffset(2001, 2, 3, 4, 5, 6, 789, TimeSpan.Zero);
        var later = new FrozenTime(at.AddYears(20));
        var frozen = new FrozenTime(at);

        _ = OrderedGuid.New(later);
        var first = OrderedGuid.New(frozen);

        Assert.Equal(at.ToUnixTimeMilliseconds(), Millisecond(first));
        Assert.Equal(0, Counter(first));
    }

    [Fact]
    public void Rask_s_clock_as_a_time_source_is_the_sequence_New_runs()
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(1);
        using var time = AmbientClock.Use(new FrozenTime(at));

        var made = Enumerable.Range(0, 100)
            .Select(static i => i % 2 == 0 ? OrderedGuid.New() : OrderedGuid.New(Clock.TimeProvider)).ToList();

        Assert.Equal(made.Order(), made);
        Assert.Equal(made.Count, made.Distinct().Count());
        Assert.All(made, id => Assert.True(Millisecond(id) >= at.ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void A_numbered_row_s_key_reads_as_its_millisecond_then_its_number()
    {
        var at = new DateTimeOffset(2026, 11, 27, 9, 30, 0, 123, TimeSpan.Zero);

        var first = OrderedGuid.Of(at, 41);
        var next = OrderedGuid.Of(at, 42);
        var later = OrderedGuid.Of(at.AddMilliseconds(1), 1);

        Assert.Equal(new Guid("01a21c57-1a3b-7000-8000-000000000029"), first);
        Assert.Equal(7, first.Version);
        Assert.True(first < next);
        Assert.True(next < later);
        Assert.Equal(first, OrderedGuid.Of(at, 41));
    }

    private static long Millisecond(Guid id) => BinaryPrimitives.ReadInt64BigEndian(id.ToByteArray(bigEndian: true)) >>> 16;

    private static long Counter(Guid id) => BinaryPrimitives.ReadInt64BigEndian(id.ToByteArray(bigEndian: true)) & 0xFFF;

    private sealed class FrozenTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // PostgreSQL compares two uuids byte by byte, unsigned, in their written (big-endian) order.
    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
