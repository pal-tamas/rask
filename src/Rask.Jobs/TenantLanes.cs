using System.Globalization;
using Rask.Data;

namespace Rask.Background;

/// <summary>
///     The lane of the tenant in flight — what <c>.InTenantLane()</c> runs a job in, and what <c>Jobs.TenantLane()</c> names
///     for <c>Jobs.Lane(…)</c>: the lane's name, then the tenant's key, so one tenant's work runs one at a time while every
///     other tenant's runs beside it.
/// </summary>
internal static class TenantLanes
{
    /// <summary>The lane a job without a name of its own runs in, per tenant.</summary>
    public const string Default = "tenant";

    /// <summary>
    ///     <paramref name="lane" /> (or <see cref="Default" />) for the tenant in flight. Throws, naming
    ///     <paramref name="call" /> as its author wrote it, when there is none — there would be no tenant to run one at a time for.
    /// </summary>
    public static string Of(string? lane, string call)
    {
        if (lane is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(lane);
        }

        var tenant = Tenant.Key
            ?? throw new InvalidOperationException(
                $"{call} names the lane of the tenant in flight, and no tenant is in flight here: word it where a tenant is (a "
                + "signed-in request of a tenant, Tenant.Use(id)), or name a lane of your own with .InLane(…).");
        return string.Create(CultureInfo.InvariantCulture, $"{lane ?? Default}/{tenant:N}");
    }
}
