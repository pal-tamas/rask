namespace Rask.SQLite.Crdt.Sync;

/// <summary>Where in the bucket the shared database lives, and how much goes in one object.</summary>
public sealed class CrdtSyncOptions
{
    /// <summary>
    ///     Key prefix everything is written under. One database per prefix, so several can share a bucket.
    /// </summary>
    public string Prefix { get; set; } = "crdt/";

    /// <summary>
    ///     Most changes in a single uploaded object. Default 5000.
    /// </summary>
    /// <remarks>
    ///     Object storage charges per request, so one object per change would be the expensive shape and
    ///     one object per sync would be the fragile one — a partial download of a huge object wastes the
    ///     whole transfer, and a replica that has been offline for a month would produce it. Batching
    ///     bounds both.
    /// </remarks>
    public int MaxChangesPerObject { get; set; } = 5000;

    internal string PeersPrefix => Normalized;

    internal string Normalized => Prefix.Length == 0 || Prefix.EndsWith('/') ? Prefix : Prefix + "/";

    internal void Validate()
    {
        if (MaxChangesPerObject <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(CrdtSyncOptions)}.{nameof(MaxChangesPerObject)} must be greater than zero.");
        }
    }
}
