using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Tests.App;

/// <summary>
///     <c>AddRask(timeZone: "Europe/Budapest")</c>: the clock on the app's wall, named once (#1290). Each test puts the
///     process-wide zone back, and nothing else in this assembly reads it.
/// </summary>
public sealed class AppTimeZoneLineTests
{
    [Fact]
    public void The_zone_named_in_the_line_is_the_wall_clock_s()
    {
        var services = new ServiceCollection();

        try
        {
            services.AddRask(timeZone: "Europe/Budapest");

            Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest").Id, Clock.Zone.Id);
        }
        finally
        {
            AmbientClock.ConfiguredZone = null;
        }
    }

    [Fact]
    public void A_zone_the_machine_does_not_know_fails_the_line_that_named_it()
    {
        var services = new ServiceCollection();

        var refusal = Assert.Throws<ArgumentException>(() => services.AddRask(timeZone: "Europe/Atlantis"));

        Assert.StartsWith("AddRask(timeZone: \"Europe/Atlantis\") names no time zone", refusal.Message, StringComparison.Ordinal);
        Assert.Null(AmbientClock.ConfiguredZone);
    }
}
