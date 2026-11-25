namespace Rask.Auth.Tests;

/// <summary>How long the throttle holds a key, and how many keys it holds.</summary>
public sealed class AuthThrottleTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_held_client_is_free_once_its_oldest_failure_leaves_the_window_and_one_more_failure_holds_it_again()
    {
        var clock = new MutableClock(Start);
        var throttle = new AuthThrottle(clock) { Limit = 2, Window = TimeSpan.FromMinutes(1) };
        throttle.Begin("k").Fail();
        clock.Advance(TimeSpan.FromSeconds(30));
        throttle.Begin("k").Fail();
        var heldAtTheLimit = throttle.Begin("k").IsThrottled;

        clock.Advance(TimeSpan.FromSeconds(30));
        var freed = throttle.Begin("k");
        var freedIsThrottled = freed.IsThrottled;
        freed.Fail();

        Assert.True(heldAtTheLimit);
        Assert.False(freedIsThrottled);
        Assert.True(throttle.Begin("k").IsThrottled);
    }

    [Fact]
    public void A_spray_of_distinct_keys_keeps_the_throttle_at_its_bound_letting_go_of_the_stalest_first()
    {
        var clock = new MutableClock(Start);
        var throttle = new AuthThrottle(clock) { Limit = 1, Window = TimeSpan.FromHours(1), MostKeys = 100 };

        for (var i = 0; i < 150; i++)
        {
            throttle.Begin("key-" + i).Fail();
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.InRange(throttle.KeyCount, 1, 100);
        Assert.False(throttle.Begin("key-0").IsThrottled);
        Assert.True(throttle.Begin("key-149").IsThrottled);
    }
}
