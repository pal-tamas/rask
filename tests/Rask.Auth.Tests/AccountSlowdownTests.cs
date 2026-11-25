namespace Rask.Auth.Tests;

/// <summary>How long an attempt at an account waits, by how often it has just failed.</summary>
public sealed class AccountSlowdownTests
{
    [Fact]
    public void The_first_few_failures_cost_nothing_then_each_doubles_the_wait_up_to_the_cap()
    {
        var slowdown = new AccountSlowdown(TimeProvider.System) { Free = 2, Cap = TimeSpan.FromSeconds(4) };
        var waits = new List<TimeSpan>();

        for (var i = 0; i < 6; i++)
        {
            waits.Add(slowdown.DelayFor("ada"));
            slowdown.Failed("ada");
        }

        Assert.Equal([0, 0, 1, 2, 4, 4], waits.Select(w => w.TotalSeconds));
    }

    [Fact]
    public void A_right_password_clears_the_count()
    {
        var slowdown = new AccountSlowdown(TimeProvider.System) { Free = 0 };
        slowdown.Failed("ada");

        slowdown.Succeeded("ada");

        Assert.Equal(TimeSpan.Zero, slowdown.DelayFor("ada"));
    }

    [Fact]
    public void The_wait_is_gone_once_the_window_has_passed_with_no_failure()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var slowdown = new AccountSlowdown(clock);
        for (var i = 0; i < slowdown.Free + 1; i++)
        {
            slowdown.Failed("ada");
        }

        var waitAfterTheFailures = slowdown.DelayFor("ada");

        clock.Advance(slowdown.Window);

        Assert.True(waitAfterTheFailures > TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, slowdown.DelayFor("ada"));
    }

    [Fact]
    public void A_spray_of_distinct_accounts_keeps_the_slowdown_at_its_bound_letting_go_of_the_stalest_first()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var slowdown = new AccountSlowdown(clock) { Free = 0, Window = TimeSpan.FromHours(1), MostKeys = 100 };

        for (var i = 0; i < 150; i++)
        {
            slowdown.Failed("account-" + i);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.InRange(slowdown.KeyCount, 1, 100);
        Assert.Equal(TimeSpan.Zero, slowdown.DelayFor("account-0"));
        Assert.True(slowdown.DelayFor("account-149") > TimeSpan.Zero);
    }
}
