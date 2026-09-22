namespace Rask.Testing.Tests;

// One frozen clock, read everywhere the app reads time — so a test moves all of it with one line.
public sealed class FakeClockTests
{
    private static readonly DateTimeOffset Monday9am = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fake_clock_freezes_Now_and_the_unit_literals()
    {
        using var clock = Clock.Fake(at: Monday9am);

        var now = Clock.Now;

        Assert.Equal(Monday9am, now);
        Assert.Equal(Monday9am.AddDays(-3), 3.Days.Ago);
    }

    [Fact]
    public void Advancing_the_clock_moves_everything_that_reads_it()
    {
        using var clock = Clock.Fake(at: Monday9am);

        clock.Advance(2.Hours);

        Assert.Equal(Monday9am + 2.Hours, Clock.Now);
        Assert.Equal(Monday9am + 2.Hours, Clock.TimeProvider.GetUtcNow());
        Assert.Equal(Monday9am + 3.Hours, 1.Hour.FromNow);
    }

    [Fact]
    public void Disposing_the_fake_puts_real_time_back()
    {
        var clock = Clock.Fake(at: Monday9am);

        clock.Dispose();

        Assert.True(Clock.Now > Monday9am.AddYears(-1) && Clock.Now.Year >= 2026 && Clock.Now != Monday9am);
    }

    [Fact]
    public void A_clock_only_moves_forward()
    {
        using var clock = Clock.Fake(at: Monday9am);

        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1.Minute));

        Assert.Contains("only moves forward", failure.Message);
    }
}
