using System.Globalization;
using Rask.Core.Browser;
using Rask.SQLite.Snapshots;

namespace Rask.SQLite.Browser;

/// <summary>
///     An <see cref="ISqliteSnapshotStore" /> that keeps snapshots in IndexedDB — the browser's answer to
///     the directory the default store writes to.
/// </summary>
/// <remarks>
///     Stored through <see cref="IKeyValueStore.SetBytesAsync" />, so a database costs its own size in
///     quota rather than a third more. Snapshot names are <c>{stem}-{yyyyMMdd-HHmmssfff}.db</c>, which
///     sorts lexicographically in timestamp order — that is what lets retention and "newest first" work
///     without an index, over a key/value store that offers nothing but a list of keys.
/// </remarks>
internal sealed class IndexedDbSnapshotStore(IIndexedDb indexedDb, string storeName) : ISqliteSnapshotStore
{
    private IKeyValueStore? _store;

    /// <inheritdoc />
    public async Task SaveAsync(string sourceFilePath, string snapshotName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);

        var bytes = await File.ReadAllBytesAsync(sourceFilePath, cancellationToken).ConfigureAwait(false);
        var store = await OpenAsync().ConfigureAwait(false);
        await store.SetBytesAsync(snapshotName, bytes).ConfigureAwait(false);

        // The snapshotter hands us a temp file it expects to be consumed. In a browser that file is in the
        // runtime's in-memory filesystem, so leaving it behind spends the tab's heap, not disk.
        File.Delete(sourceFilePath);
    }

    /// <inheritdoc />
    public async Task PruneAsync(int retain, CancellationToken cancellationToken)
    {
        var store = await OpenAsync().ConfigureAwait(false);
        var keys = await store.KeysAsync().ConfigureAwait(false);

        foreach (var stale in Ordered(keys).Skip(Math.Max(retain, 1)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await store.DeleteAsync(stale).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SqliteSnapshotInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var store = await OpenAsync().ConfigureAwait(false);
        var keys = await store.KeysAsync().ConfigureAwait(false);
        var infos = new List<SqliteSnapshotInfo>();

        foreach (var key in Ordered(keys))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The size costs a read of the value: IndexedDB reports no metadata of its own, and a
            // listing that lied about size would be worse than one that is a little slower.
            var bytes = await store.GetBytesAsync(key).ConfigureAwait(false);
            infos.Add(new SqliteSnapshotInfo(key, bytes?.LongLength ?? 0, ParseTimestamp(key)));
        }

        return infos;
    }

    /// <summary>The newest snapshot's bytes, or <c>null</c> when this store holds none.</summary>
    public async Task<byte[]?> ReadNewestAsync()
    {
        var store = await OpenAsync().ConfigureAwait(false);
        var keys = await store.KeysAsync().ConfigureAwait(false);
        var newest = Ordered(keys).FirstOrDefault();

        return newest is null ? null : await store.GetBytesAsync(newest).ConfigureAwait(false);
    }

    // Newest first. Ordinal, because the timestamp format is fixed-width and zero-padded, so byte order
    // IS chronological order — and a culture-aware comparison over it would only be slower and less stable.
    private static IEnumerable<string> Ordered(IEnumerable<string> keys) =>
        keys.OrderByDescending(static k => k, StringComparer.Ordinal);

    private static DateTime ParseTimestamp(string snapshotName)
    {
        // "{stem}-{yyyyMMdd-HHmmssfff}.db" — the stem may itself contain dashes, so anchor on the end.
        var name = snapshotName.EndsWith(".db", StringComparison.Ordinal) ? snapshotName[..^3] : snapshotName;
        var dash = name.LastIndexOf('-');

        if (dash > 0 && dash >= 9)
        {
            var candidate = name[(dash - 8)..];
            if (DateTime.TryParseExact(
                    candidate,
                    "yyyyMMdd-HHmmssfff",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed;
            }
        }

        // A snapshot written by some other producer still lists; it just cannot claim a creation time.
        return default;
    }

    // IIndexedDb caches the underlying connection, but OpenStoreAsync still crosses the JS boundary, so
    // hold the handle rather than paying for it on every save in a snapshot loop.
    private async Task<IKeyValueStore> OpenAsync() => _store ??= await indexedDb.OpenStoreAsync(storeName).ConfigureAwait(false);
}
