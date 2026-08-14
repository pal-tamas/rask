namespace Rask.SQLite.Crdt.Sync.Tests;

/// <summary>
///     A replica's log, in memory. Models the two properties the bucket layout is built on — a change
///     keeps its originating <c>site_id</c> forever, and applying one stamps it with the RECEIVING
///     replica's next version — because a fake that got either wrong would make the engine's tests agree
///     with a design that cannot work against the real extension.
/// </summary>
internal sealed class FakeChangeFeed(string site) : ICrdtChangeFeed
{
    private readonly List<CrdtChange> _log = [];
    private readonly byte[] _site = Convert.FromHexString(site);
    private long _version;

    public IReadOnlyList<CrdtChange> Log => _log;

    public Task<byte[]> GetSiteIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_site);

    public Task<long> GetDbVersionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_version);

    public Task<IReadOnlyList<CrdtChange>> ReadChangesAsync(
        long sinceDbVersion = -1, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CrdtChange>>(
            [.. _log.Where(c => c.DbVersion > sinceDbVersion)]);

    public Task<IReadOnlyList<CrdtChange>> ReadLocalChangesAsync(
        long sinceDbVersion = -1, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CrdtChange>>(
            [.. _log.Where(c => c.DbVersion > sinceDbVersion && c.SiteId.SequenceEqual(_site))]);

    public Task ApplyChangesAsync(
        IEnumerable<CrdtChange> changes, CancellationToken cancellationToken = default)
    {
        // One version for the whole batch, matching cr-sqlite's per-transaction assignment — and the
        // site id travels untouched, which is what lets a replica tell its own work from a peer's.
        var batch = changes.ToList();
        if (batch.Count == 0)
        {
            return Task.CompletedTask;
        }

        _version++;
        foreach (var change in batch)
        {
            if (_log.Any(c => c.SiteId.SequenceEqual(change.SiteId)
                              && c.ColumnName == change.ColumnName
                              && c.PrimaryKey.SequenceEqual(change.PrimaryKey)
                              && c.ColumnVersion == change.ColumnVersion))
            {
                continue;   // idempotent, as the real feed is
            }

            _log.Add(change with { DbVersion = _version });
        }

        return Task.CompletedTask;
    }

    /// <summary>A local edit, as SaveChanges would produce.</summary>
    public void Write(string table, string column, object? value)
    {
        _version++;
        _log.Add(new CrdtChange(
            table,
            [(byte)_log.Count],
            column,
            value,
            ColumnVersion: _version,
            DbVersion: _version,
            SiteId: _site,
            CausalLength: 1,
            Sequence: 0));
    }
}
