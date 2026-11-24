using Rask.Data;
using Rask.Values;

namespace Rask.Testing;

/// <summary>A moment a <see cref="Moment" /> accepts: its earliest, or now on the wall clock within its latest.</summary>
internal static class MomentSample
{
    public static DateTime For(MomentRules rules)
    {
        var now = Clock.Wall;
        var latest = rules.NotAfter;
        if (rules.NotAfterNow && (latest is not { } declared || now < declared))
        {
            latest = now;
        }

        return rules.NotBefore ?? latest ?? now;
    }
}
