using Jint.Runtime;

namespace Rask.TestSupport.ScriptFixtures;

/// <summary>
///     <c>Date</c> on the fixture's virtual clock, so a module that measures elapsed time with <c>Date.now()</c> sees
///     the same time pass as one that waits on a timer.
/// </summary>
internal sealed class FixtureTimeSystem(FixtureTimers timers, DateTimeOffset origin) : ITimeSystem
{
    private readonly DefaultTimeSystem _calendar = new(TimeZoneInfo.Utc, System.Globalization.CultureInfo.InvariantCulture);

    public TimeZoneInfo DefaultTimeZone => _calendar.DefaultTimeZone;

    public DateTimeOffset GetUtcNow() => origin.AddMilliseconds(timers.Now);

    public bool TryParse(string date, out long epochMilliseconds) => _calendar.TryParse(date, out epochMilliseconds);

    public TimeSpan GetUtcOffset(long epochMilliseconds) => _calendar.GetUtcOffset(epochMilliseconds);
}
