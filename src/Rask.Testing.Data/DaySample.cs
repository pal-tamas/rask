using Rask.Data;

namespace Rask.Testing;

/// <summary>A day a <see cref="Day" /> accepts: its earliest, or today within its latest.</summary>
internal static class DaySample
{
    public static DateOnly For(DayRules rules)
    {
        var latest = rules.NotAfter;
        if (rules.NotAfterToday && (latest is not { } declared || Clock.Today < declared))
        {
            latest = Clock.Today;
        }

        return rules.NotBefore ?? latest ?? Clock.Today;
    }
}
