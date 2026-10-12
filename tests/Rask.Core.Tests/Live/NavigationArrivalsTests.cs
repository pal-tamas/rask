using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public sealed class NavigationArrivalsTests
{
    [Fact]
    public void A_navigation_that_is_read_ends_the_wait_taken_before_it_and_not_the_one_taken_after()
    {
        using var arrivals = new NavigationArrivals();
        var before = arrivals.Superseded;

        arrivals.Read();
        var after = arrivals.Superseded;

        before.WaitHandle.WaitOne(WaitFor.HangCeiling);
        Assert.True(before.IsCancellationRequested);
        Assert.False(after.IsCancellationRequested);
    }

    [Fact]
    public void Of_two_navigations_read_before_either_began_only_the_newer_takes_its_turn()
    {
        using var arrivals = new NavigationArrivals();
        arrivals.Read();
        arrivals.Read();

        var olderStarts = arrivals.Starts();
        var queuedBetween = arrivals.AnyQueued;
        var newerStarts = arrivals.Starts();

        Assert.False(olderStarts);
        Assert.True(queuedBetween);
        Assert.True(newerStarts);
        Assert.False(arrivals.AnyQueued);
    }

    [Fact]
    public void A_wait_taken_after_the_session_has_gone_is_already_over()
    {
        var arrivals = new NavigationArrivals();

        arrivals.Dispose();

        Assert.True(arrivals.Superseded.IsCancellationRequested);
    }
}
