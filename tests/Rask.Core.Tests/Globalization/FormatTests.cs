using System.Globalization;

namespace Rask.Core.Tests.Globalization;

[Collection(CultureCollection.Name)]
public sealed class FormatTests : IDisposable
{
    private static readonly CultureInfo Hungarian = CultureInfo.GetCultureInfo("hu-HU");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    [Fact]
    public void A_day_a_moment_and_a_number_are_written_as_Hungarian_writes_them()
    {
        CultureInfo.CurrentCulture = Hungarian;
        var space = Hungarian.NumberFormat.NumberGroupSeparator;

        var day = Format.Day(new DateOnly(2026, 10, 9));
        var moment = Format.Moment(new DateTime(2026, 10, 9, 14, 35, 0));
        var whole = Format.Number(24000);
        var litres = Format.Number(1764.600000000m);
        var amount = Format.Number(1764.6m, 2);

        Assert.Equal("2026. 10. 09.", day);
        Assert.Equal("2026. 10. 09. 14:35", moment);
        Assert.Equal($"24{space}000", whole);
        Assert.Equal($"1{space}764,6", litres);
        Assert.Equal($"1{space}764,60", amount);
    }

    [Fact]
    public void A_day_a_moment_and_a_number_are_written_as_English_writes_them()
    {
        CultureInfo.CurrentCulture = English;

        var day = Format.Day(new DateOnly(2026, 10, 9));
        var moment = Format.Moment(new DateTime(2026, 10, 9, 14, 35, 0));
        var whole = Format.Number(24000);
        var litres = Format.Number(1764.6);
        var amount = Format.Number(1764.6, 2);

        Assert.Equal("10/9/2026", day);
        Assert.StartsWith("10/9/2026 2:35", moment, StringComparison.Ordinal);
        Assert.EndsWith("PM", moment, StringComparison.Ordinal);
        Assert.Equal("24,000", whole);
        Assert.Equal("1,764.6", litres);
        Assert.Equal("1,764.60", amount);
    }

    [Fact]
    public void A_UTC_moment_is_written_on_the_wall_clock_and_an_unspecified_one_as_it_is()
    {
        CultureInfo.CurrentCulture = Hungarian;
        using var zone = AmbientClock.UseZone(Budapest);
        var lateUtc = new DateTimeOffset(2026, 10, 9, 22, 35, 0, TimeSpan.Zero);

        var moment = Format.Moment(lateUtc);
        var day = Format.Day(lateUtc);
        var utcDateTime = Format.Moment(lateUtc.UtcDateTime);
        var wallDateTime = Format.Moment(new DateTime(2026, 10, 9, 22, 35, 0, DateTimeKind.Unspecified));

        Assert.Equal("2026. 10. 10. 0:35", moment);
        Assert.Equal("2026. 10. 10.", day);
        Assert.Equal("2026. 10. 10. 0:35", utcDateTime);
        Assert.Equal("2026. 10. 09. 22:35", wallDateTime);
    }
}
