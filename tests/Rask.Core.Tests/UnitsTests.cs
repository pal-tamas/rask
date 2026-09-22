namespace Rask.Core.Tests;

public sealed class UnitsTests
{
    [Fact]
    public void Whole_numbers_read_as_the_durations_they_name()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), 250.Milliseconds);
        Assert.Equal(TimeSpan.FromSeconds(3), 3.Seconds);
        Assert.Equal(TimeSpan.FromMinutes(10), 10.Minutes);
        Assert.Equal(TimeSpan.FromHours(24), 24.Hours);
        Assert.Equal(TimeSpan.FromDays(30), 30.Days);
        Assert.Equal(TimeSpan.FromDays(14), 2.Weeks);
    }

    [Fact]
    public void The_singular_is_the_same_value_as_the_plural()
    {
        // Writing the plural with a count of one is exactly what RASK092 flags; here it is the point.
#pragma warning disable RASK092

        Assert.Equal(1.Milliseconds, 1.Millisecond);
        Assert.Equal(1.Seconds, 1.Second);
        Assert.Equal(1.Minutes, 1.Minute);
        Assert.Equal(1.Hours, 1.Hour);
        Assert.Equal(1.Days, 1.Day);
        Assert.Equal(1.Weeks, 1.Week);
        Assert.Equal(1.Bytes, 1.Byte);
        Assert.Equal(1.Kilobytes, 1.Kilobyte);
        Assert.Equal(1.Megabytes, 1.Megabyte);
        Assert.Equal(1.Gigabytes, 1.Gigabyte);
#pragma warning restore RASK092
    }

    [Fact]
    public void Fractions_read_as_durations()
    {
        Assert.Equal(TimeSpan.FromMinutes(90), 1.5.Hours);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), 1.5.Seconds);
        Assert.Equal(TimeSpan.FromHours(12), 0.5.Days);
        Assert.Equal(TimeSpan.FromDays(10.5), 1.5.Weeks);
    }

    [Fact]
    public void Sizes_are_binary()
    {
        Assert.Equal(512L, 512.Bytes);
        Assert.Equal(4096L, 4.Kilobytes);
        Assert.Equal(50L * 1024 * 1024, 50.Megabytes);
        Assert.Equal(1024L * 1024 * 1024, 1.Gigabyte);
    }

    [Fact]
    public void An_int_size_past_two_gigabytes_does_not_wrap()
    {
        // 4 × 1024³ overflows an int; the product is taken in long.
        Assert.Equal(4L * 1024 * 1024 * 1024, 4.Gigabytes);
        Assert.Equal(8L * 1024 * 1024 * 1024 * 1024, (8L * 1024).Gigabytes);
    }

    [Fact]
    public void A_size_past_a_long_throws_rather_than_wrapping() =>
        Assert.Throws<OverflowException>(() => long.MaxValue.Kilobytes);

    [Fact]
    public void Ago_and_FromNow_read_the_clock_in_scope()
    {
        var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        using (AmbientClock.Use(new FixedClock(now)))
        {
            Assert.Equal(now.AddDays(-3), 3.Days.Ago);
            Assert.Equal(now.AddHours(2), 2.Hours.FromNow);
        }
    }

    [Fact]
    public async Task Two_flows_each_see_only_their_own_frozen_clock()
    {
        var first = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var gate = new Barrier(2);

        var seen = await Task.WhenAll(ReadUnder(first), ReadUnder(second));

        Assert.Equal([first, second], seen);
        Assert.True(0.Seconds.FromNow.Year > 2010);

        Task<DateTimeOffset> ReadUnder(DateTimeOffset at) => Task.Run(() =>
        {
            using var _ = AmbientClock.Use(new FixedClock(at));
            gate.SignalAndWait();   // both clocks are set before either is read
            return 0.Seconds.FromNow;
        });
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
