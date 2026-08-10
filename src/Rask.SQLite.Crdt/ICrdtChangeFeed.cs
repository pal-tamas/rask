namespace Rask.SQLite.Crdt;

/// <summary>
///     The replication log a transport reads from and writes back to.
/// </summary>
/// <remarks>
///     An interface so a transport can be written and tested without a database or the native extension
///     behind it — the two are independent concerns, and requiring cr-sqlite to test a bucket layout
///     would mean the layout only ever got tested where the binary happened to exist.
///     <see cref="CrdtChangeFeed" /> is the real implementation.
/// </remarks>
public interface ICrdtChangeFeed
{
    /// <summary>This replica's identity — the <c>site_id</c> stamped on every change it makes.</summary>
    Task<byte[]> GetSiteIdAsync(CancellationToken cancellationToken = default);

    /// <summary>This replica's current version. Local to this database — see <see cref="CrdtChangeFeed" />.</summary>
    Task<long> GetDbVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Every change above <paramref name="sinceDbVersion" />, including peers' work.</summary>
    Task<IReadOnlyList<CrdtChange>> ReadChangesAsync(
        long sinceDbVersion = -1, CancellationToken cancellationToken = default);

    /// <summary>Only what this replica originated — what to publish.</summary>
    Task<IReadOnlyList<CrdtChange>> ReadLocalChangesAsync(
        long sinceDbVersion = -1, CancellationToken cancellationToken = default);

    /// <summary>Merges changes from a peer. Applying twice changes nothing.</summary>
    Task ApplyChangesAsync(IEnumerable<CrdtChange> changes, CancellationToken cancellationToken = default);
}
