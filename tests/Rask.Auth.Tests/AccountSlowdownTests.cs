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
}
