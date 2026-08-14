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

    /// <summary>
    ///     Compact this replica's own objects into one during a sync once it has published more than
    ///     this many. Default 50; zero or less turns it off.
    /// </summary>
    /// <remarks>
    ///     What makes this cheap is that the change feed is <b>current state, not history</b>: it holds
    ///     one entry per (row, column) with the value that won, so editing the same field a thousand
    ///     times leaves one entry and a deleted row collapses to a single tombstone. Republishing
    ///     everything therefore costs the size of the database rather than the number of edits ever
    ///     made — which is what lets a device fold its whole prefix into one object.
    /// </remarks>
    public int CompactAfterObjects { get; set; } = 50;

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
