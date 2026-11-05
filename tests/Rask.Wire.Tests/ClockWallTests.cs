namespace Rask.Wire.Tests;

// One class, so its tests — two of which set the process-wide configured zone — never run beside each other.
public sealed class ClockWallTests
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    [Fact]
    public void The_configured_zone_s_wall_clock_springs_an_hour_forward_across_the_summer_time_change()
    {
        // Budapest moves from 02:00 CET to 03:00 CEST at 01:00 UTC on 29 March 2026.
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero));
        using var time = AmbientClock.Use(clock);
        AmbientClock.Configure("Europe/Budapest", "AddRask(timeZone: \"Europe/Budapest\")");

        try
        {
            var before = Clock.Wall;
            clock.Now = clock.Now.AddHours(1);
            var after = Clock.Wall;

            Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0), before);
            Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), after);
            Assert.Equal(DateTimeKind.Unspecified, after.Kind);
            Assert.Equal(new DateOnly(2026, 3, 29), Clock.Today);
            Assert.Equal(Budapest.Id, Clock.Zone.Id);
        }
        finally
        {
            AmbientClock.ConfiguredZone = null;
        }
    }

    [Fact]
    public void The_wall_clock_falls_back_an_hour_in_autumn_and_its_day_turns_at_the_zone_s_midnight()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 24, 22, 30, 0, TimeSpan.Zero));
        using var time = AmbientClock.Use(clock);
        using var zone = AmbientClock.UseZone(Budapest);

        var saturdayNight = Clock.Wall;
        clock.Now = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero);
        var afterTheChange = Clock.Wall;

        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0), saturdayNight);
        Assert.Equal(new DateTime(2026, 10, 25, 2, 30, 0), afterTheChange);
        Assert.Equal(new DateOnly(2026, 10, 25), Clock.Today);
    }

    [Fact]
    public void Without_a_configured_zone_the_wall_clock_is_the_time_provider_s_own_local_zone()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)) { Zone = Budapest };
        using var time = AmbientClock.Use(clock);

        var wall = Clock.Wall;

        Assert.Equal(new DateTime(2026, 7, 1, 14, 0, 0), wall);
        Assert.Equal(Budapest.Id, Clock.Zone.Id);
    }

    [Fact]
    public void A_zone_the_machine_does_not_know_is_refused_naming_the_call_that_named_it()
    {
        var call = "AddRask(timeZone: \"Europe/Atlantis\")";

        var refusal = Assert.Throws<ArgumentException>(() => AmbientClock.Configure("Europe/Atlantis", call));

        Assert.StartsWith(call + " names no time zone this machine knows.", refusal.Message, StringComparison.Ordinal);
        Assert.Null(AmbientClock.ConfiguredZone);
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => Zone;
    }
}
