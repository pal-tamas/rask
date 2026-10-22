using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public sealed class NavigationArrivalsTests
{
    [Fact]
    public void Of_two_navigations_read_before_either_began_only_the_newer_takes_its_turn()
    {
        var arrivals = new NavigationArrivals();
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
}
