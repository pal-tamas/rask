using Rask.Cqrs;
using Rask.Values;
using Rask.Wire;

namespace Rask.Core.Tests.Values;

public sealed partial record ReadAt(DateTime Value) : Moment(Value);

public sealed partial record ReadSince(DateTime Value) : Moment(Value, NotBefore: new DateTime(2026, 1, 1));

public sealed partial record ReadOn(DateOnly Value) : Day(Value);

// Text read onto the app's wall clock: an offset converted from, none taken as the wall's, culture-invariant.
public sealed class WallParseTests
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    [Theory]
    [InlineData("2026-11-23T08:35:51.000+01:00", "2026-11-23T08:35:51")]
    [InlineData("2026-07-08T10:00:00.000+02:00", "2026-07-08T10:00:00")]
    [InlineData("2026-07-08T08:00:00Z", "2026-07-08T10:00:00")]
    [InlineData("2026-01-14T08:00:00Z", "2026-01-14T09:00:00")]
    [InlineData("2026-03-29T01:00:00Z", "2026-03-29T03:00:00")]
    [InlineData("2026-10-25T00:59:59Z", "2026-10-25T02:59:59")]
    [InlineData("2026-10-25T01:00:00Z", "2026-10-25T02:00:00")]
    [InlineData("2026-07-08T10:00:00", "2026-07-08T10:00:00")]
    [InlineData(" 2026-07-08 10:00 ", "2026-07-08T10:00:00")]
    public void A_moment_is_read_as_the_wall_clock_of_the_app_s_zone_showed_it(string written, string wall)
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var read = ReadAt.Parse(written);

        Assert.Equal(wall, read.ToString());
        Assert.Equal(DateTimeKind.Unspecified, read.Value.Kind);
    }

    [Fact]
    public void A_moment_in_a_format_of_its_own_is_read_in_exactly_that_format()
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var read = ReadAt.Parse("2026.11.23. 08:35", "yyyy.MM.dd. HH:mm");
        var offset = ReadAt.Parse("23/11/2026 07:35 +00:00", "dd/MM/yyyy HH:mm zzz");
        var refused = ReadAt.TryParse("2026-11-23T08:35", "yyyy.MM.dd. HH:mm", out var none);

        Assert.Equal(new DateTime(2026, 11, 23, 8, 35, 0), read.Value);
        Assert.Equal(new DateTime(2026, 11, 23, 8, 35, 0), offset.Value);
        Assert.False(refused);
        Assert.Null(none);
    }

    [Fact]
    public void Text_that_is_no_moment_is_a_format_error_and_a_moment_a_rule_refuses_is_a_validation_error()
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var unreadable = Assert.Throws<FormatException>(() => ReadAt.Parse("tegnap"));
        var early = Assert.Throws<RaskValidationException>(() => ReadSince.Parse("2025-12-31T23:00:00", field: "At"));
        var tried = (ReadAt.TryParse("tegnap", out var none), ReadSince.TryParse("2025-12-31T23:00:00", out var refused), ReadAt.TryParse(null, out var empty));

        Assert.Contains("'tegnap' is not a moment", unreadable.Message, StringComparison.Ordinal);
        Assert.Equal(["At"], early.Failures.Single().Fields);
        Assert.Equal((false, false, false), tried);
        Assert.Equal((null, null, null), (none, refused, empty));
    }

    [Fact]
    public void A_moment_is_read_the_same_whatever_the_thread_s_culture()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("hu-HU");

        try
        {
            var read = ReadAt.Parse("2026-11-23T08:35:51");
            var day = ReadOn.Parse("2026-11-23");

            Assert.Equal(new DateTime(2026, 11, 23, 8, 35, 51), read.Value);
            Assert.Equal(new DateOnly(2026, 11, 23), day.Value);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("2026-11-23", null, "2026-11-23")]
    [InlineData("2026-11-23T23:30:00Z", null, "2026-11-24")]
    [InlineData("2026-11-23T23:30:00", null, "2026-11-23")]
    [InlineData("2026.11.23.", "yyyy.MM.dd.", "2026-11-23")]
    public void A_day_is_read_as_the_wall_calendar_of_the_app_s_zone_shows_it(string written, string? format, string day)
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var read = ReadOn.Parse(written, format);
        var tried = ReadOn.TryParse("holnap", out var none);

        Assert.Equal(day, read.ToString());
        Assert.False(tried);
        Assert.Null(none);
    }

    [Fact]
    public void A_moment_at_any_offset_is_shown_on_the_wall_clock_of_the_app_s_zone()
    {
        using var zone = AmbientClock.UseZone(Budapest);

        var winter = Clock.WallAt(new DateTimeOffset(2026, 11, 23, 7, 35, 0, TimeSpan.Zero));
        var stamp = Clock.WallAt(DateTime.SpecifyKind(new DateTime(2026, 7, 8, 8, 0, 0), DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 11, 23, 8, 35, 0), winter);
        Assert.Equal(new DateTime(2026, 7, 8, 10, 0, 0), stamp);
        Assert.Equal(DateTimeKind.Unspecified, winter.Kind);
    }
}
