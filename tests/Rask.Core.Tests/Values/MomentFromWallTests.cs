using System.Globalization;
using Rask.Values;
using Rask.Wire;

namespace Rask.Core.Tests.Values;

public sealed partial record PumpReading(DateTime Value) : Moment(Value);

// A wall-clock time of a zone made into the instant it names, by the one rule (WallClock): the first occurrence of an
// hour the clock shows twice, a skipped hour moved forward — in Europe/Budapest, where both happen every year.
public sealed class MomentFromWallTests
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");

    [Theory]
    [InlineData("2026-01-14T09:00:00", "2026-01-14T08:00:00Z")]
    [InlineData("2026-07-08T10:00:00", "2026-07-08T08:00:00Z")]
    [InlineData("2026-03-29T01:59:59", "2026-03-29T00:59:59Z")]
    [InlineData("2026-03-29T03:00:00", "2026-03-29T01:00:00Z")]
    [InlineData("2026-10-25T01:59:59", "2026-10-24T23:59:59Z")]
    [InlineData("2026-10-25T03:00:00", "2026-10-25T02:00:00Z")]
    public void A_wall_time_the_clock_shows_once_is_the_one_instant_it_names(string wall, string instant)
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var reading = PumpReading.FromWall(DateTime.Parse(wall, CultureInfo.InvariantCulture), Budapest);

        Assert.Equal(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture), reading.Instant);
        Assert.Equal(wall, reading.ToString());
    }

    [Fact]
    public void A_wall_time_the_clock_shows_twice_when_summer_time_ends_is_its_first_occurrence()
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var reading = PumpReading.FromWall(new DateTime(2026, 10, 25, 2, 30, 0), Budapest);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), reading.Instant);
        Assert.Equal(new DateTime(2026, 10, 25, 2, 30, 0), reading.Value);
    }

    [Fact]
    public void A_wall_time_the_clock_skips_when_summer_time_begins_moves_forward_by_the_jump()
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var reading = PumpReading.FromWall(new DateTime(2026, 3, 29, 2, 30, 0), Budapest);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), reading.Instant);
        Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), reading.Value);
    }

    [Fact]
    public void Both_occurrences_of_the_repeated_hour_stay_two_moments_when_made_from_their_instants()
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var first = PumpReading.Create(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
        var second = PumpReading.Create(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));

        Assert.Equal(first.Value, second.Value);
        Assert.NotEqual(first, second);
        Assert.True(first < second);
        Assert.Equal(TimeSpan.FromHours(1), second.Instant - first.Instant);
    }

    [Fact]
    public void A_wall_time_of_another_zone_is_held_on_the_app_s_own_wall_clock()
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var reading = PumpReading.FromWall(new DateTime(2026, 7, 8, 9, 0, 0), Lisbon);

        Assert.Equal(new DateTime(2026, 7, 8, 10, 0, 0), reading.Value);
        Assert.Equal(new DateTime(2026, 7, 8, 9, 0, 0), reading.WallIn(Lisbon));
        Assert.Equal(DateTimeKind.Unspecified, reading.WallIn(Lisbon).Kind);
    }

    [Fact]
    public void Without_a_zone_a_wall_time_is_read_on_the_app_s_clock()
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var reading = PumpReading.FromWall(new DateTime(2026, 7, 8, 10, 0, 0));

        Assert.Equal(new DateTimeOffset(2026, 7, 8, 8, 0, 0, TimeSpan.Zero), reading.Instant);
        Assert.Equal(new DateOnly(2026, 7, 8), reading.Day);
    }

    [Fact]
    public void A_utc_time_given_as_the_wall_time_is_taken_as_the_instant_it_names()
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var reading = PumpReading.FromWall(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc), Lisbon);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), reading.Instant);
    }
}
