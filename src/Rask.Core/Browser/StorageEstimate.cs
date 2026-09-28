namespace Rask.Core.Browser;

/// <summary>An estimate of the origin's storage budget (<c>StorageManager.estimate()</c>).</summary>
/// <param name="Quota">Total bytes the origin may use (a conservative estimate).</param>
/// <param name="Usage">Bytes the origin is currently using across caches, IndexedDB, etc.</param>
public sealed record StorageEstimate(long Quota, long Usage)
{
    /// <summary>Fraction of the quota in use, <c>0</c>–<c>1</c> (<c>0</c> when the quota is unknown).</summary>
    public double UsageRatio => Quota > 0 ? (double)Usage / Quota : 0;
}
