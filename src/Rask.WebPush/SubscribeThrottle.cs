using System.Net;
using System.Threading.RateLimiting;
using Rask.Hosting.Shared;

namespace Rask.WebPush;

/// <summary>
/// How often one client may post a subscription: ten times a minute.
/// </summary>
/// <remarks>
/// A browser subscribes once and again when its keys rotate. The route is open to anyone, so this is what
/// stands between it and a loop — the row cap bounds the table, this bounds the work of getting there.
/// </remarks>
internal sealed class SubscribeThrottle : IDisposable
{
    private const int PerMinute = 10;

    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(
        static client => RateLimitPartition.GetFixedWindowLimiter(
            client,
            static _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    /// <summary>Counts one subscribe from <paramref name="address" />; false once it is over the limit.</summary>
    public bool Admits(IPAddress? address)
    {
        using var lease = _limiter.AttemptAcquire(ClientNetwork.Of(address));
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
