using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

public sealed class ReadinessPollTests
{
    [Fact]
    public async Task A_thing_already_true_is_asked_once_with_no_pause_before_it()
    {
        var clock = new SteppedClock();
        var poll = clock.Poll(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        var asks = 0;

        var ready = await poll.UntilAsync(() => Task.FromResult(++asks > 0), CancellationToken.None);

        Assert.True(ready);
        Assert.Equal(1, asks);
        Assert.Empty(clock.Pauses);
    }

    [Fact]
    public async Task It_is_asked_again_a_second_after_each_no_until_it_says_yes()
    {
        var clock = new SteppedClock();
        var poll = clock.Poll(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        var asks = 0;

        var ready = await poll.UntilAsync(() => Task.FromResult(++asks == 3), CancellationToken.None);

        Assert.True(ready);
        Assert.Equal(3, asks);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], clock.Pauses);
    }

    [Fact]
    public async Task Asking_stops_at_the_deadline_without_a_pause_past_it()
    {
        var clock = new SteppedClock();
        var poll = clock.Poll(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        var asks = 0;

        var ready = await poll.UntilAsync(() => Task.FromResult(++asks < 0), CancellationToken.None);

        Assert.False(ready);
        Assert.Equal(6, asks);
        Assert.Equal(TimeSpan.FromSeconds(5), clock.Elapsed);
    }

    [Fact]
    public async Task A_cap_on_asks_ends_the_wait_before_the_deadline()
    {
        var clock = new SteppedClock();
        var poll = new ReadinessPoll { Interval = TimeSpan.Zero, Timeout = TimeSpan.FromHours(1), MaxAttempts = 1, Clock = clock, Sleep = clock.Pause };
        var asks = 0;

        var ready = await poll.UntilAsync(() => Task.FromResult(++asks < 0), CancellationToken.None);

        Assert.False(ready);
        Assert.Equal(1, asks);
    }
}
