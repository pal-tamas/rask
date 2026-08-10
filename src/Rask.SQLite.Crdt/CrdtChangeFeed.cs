using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.SQLite.Crdt;

/// <summary>
///     Reads and applies cr-sqlite's change feed — the log a replica ships to its peers, and accepts from
///     them.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately transport-free. It hands you an ordered list of changes and takes one back; where
///         those bytes travel — an object-storage bucket, a socket, a file on a USB stick — is somebody
///         else's problem, and keeping it that way is what lets the same log work with no server at all.
///     </para>
///     <para>
///         Applying is idempotent: a change already seen is a no-op. That is what makes it safe to
///         re-send after an upload whose outcome is unknown, and it is why a replica never has to track
///         what its peers already have.
///     </para>
/// </remarks>
public sealed class CrdtChangeFeed(DbContext context) : ICrdtChangeFeed
{
    private const string Columns =
        "\"table\", pk, cid, val, col_version, db_version, site_id, cl, seq";

    /// <summary>This replica's identity — the <c>site_id</c> stamped on every change it makes.</summary>
    public async Task<byte[]> GetSiteIdAsync(CancellationToken cancellationToken = default)
    {
        await using var command = await CommandAsync("SELECT crsql_site_id();", cancellationToken)
            .ConfigureAwait(false);
        return (byte[])(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>This replica's current version — the high-water mark a peer would resume from.</summary>
    /// <remarks>
    ///     <b>Local, not global.</b> A change's <c>db_version</c> is assigned by whichever database it is
    ///     being read from, not by the replica that originated it: applying a peer's change stamps it with
    ///     <em>this</em> replica's next version. So a version is only ever meaningful against the database
    ///     it came from — it can order this replica's own publishing, but it can never say "everything
    ///     peer X has after N". Tracking what a peer has already sent is the transport's job.
    /// </remarks>
    public async Task<long> GetDbVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var command = await CommandAsync("SELECT crsql_db_version();", cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    ///     Every change in this database above <paramref name="sinceDbVersion" />, oldest first —
    ///     including ones originally made by peers.
    /// </summary>
    /// <remarks>
    ///     Asking by version rather than by time is what keeps a sync proportional to what changed instead
    ///     of to how long the database has existed. To publish, prefer
    ///     <see cref="ReadLocalChangesAsync" />.
    /// </remarks>
    public Task<IReadOnlyList<CrdtChange>> ReadChangesAsync(
        long sinceDbVersion = -1, CancellationToken cancellationToken = default) =>
        ReadAsync(sinceDbVersion, onlyLocal: false, cancellationToken);

    /// <summary>
    ///     Only the changes this replica originated, above <paramref name="sinceDbVersion" />.
    /// </summary>
    /// <remarks>
    ///     What to publish when every replica publishes its own work. A replica's feed also carries every
    ///     change it has ever accepted from a peer — still stamped with the peer's <c>site_id</c>, which is
    ///     what makes this filter possible — so publishing the unfiltered feed would have every device
    ///     re-uploading every other device's history, growing with the number of peers rather than with
    ///     what changed.
    /// </remarks>
    public Task<IReadOnlyList<CrdtChange>> ReadLocalChangesAsync(
        long sinceDbVersion = -1, CancellationToken cancellationToken = default) =>
        ReadAsync(sinceDbVersion, onlyLocal: true, cancellationToken);

    /// <summary>
    ///     Merges changes from a peer. Re-applying something already seen changes nothing, so a caller
    ///     never has to work out what is new.
    /// </summary>
    /// <remarks>
    ///     The whole batch is applied in <b>one transaction</b>. Two reasons, and the second is easy to
    ///     miss: a peer's transaction stays atomic instead of landing a column at a time, and cr-sqlite
    ///     assigns a fresh local <c>db_version</c> per transaction — so applying row by row would inflate
    ///     this replica's version by the size of every batch it ever receives.
    /// </remarks>
    public async Task ApplyChangesAsync(
        IEnumerable<CrdtChange> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var batch = changes as IReadOnlyCollection<CrdtChange> ?? [.. changes];
        if (batch.Count == 0)
        {
            return;
        }

        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        // Join the caller's transaction when there is one, so applying changes can be part of a larger
        // unit of work rather than committing underneath it.
        var ambient = context.Database.CurrentTransaction;
        var transaction = ambient ?? await context.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction.GetDbTransaction();
            command.CommandText =
                $"INSERT INTO crsql_changes ({Columns}) " +
                "VALUES ($table, $pk, $cid, $val, $cv, $dv, $site, $cl, $seq);";

            // Parameters created once and re-bound per change: the statement is prepared once rather than
            // for every column of every row.
            var table = command.Parameters.Add("$table", SqliteType.Text);
            var pk = command.Parameters.Add("$pk", SqliteType.Blob);
            var cid = command.Parameters.Add("$cid", SqliteType.Text);
            var val = command.Parameters.Add("$val", SqliteType.Text);
            var cv = command.Parameters.Add("$cv", SqliteType.Integer);
            var dv = command.Parameters.Add("$dv", SqliteType.Integer);
            var site = command.Parameters.Add("$site", SqliteType.Blob);
            var cl = command.Parameters.Add("$cl", SqliteType.Integer);
            var seq = command.Parameters.Add("$seq", SqliteType.Integer);

            foreach (var change in batch)
            {
                table.Value = change.Table;
                pk.Value = change.PrimaryKey;
                cid.Value = change.ColumnName;
                val.Value = change.Value ?? DBNull.Value;
                cv.Value = change.ColumnVersion;
                dv.Value = change.DbVersion;
                site.Value = change.SiteId;
                cl.Value = change.CausalLength;
                seq.Value = change.Sequence;

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (ambient is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (ambient is null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<IReadOnlyList<CrdtChange>> ReadAsync(
        long sinceDbVersion, bool onlyLocal, CancellationToken cancellationToken)
    {
        var mine = onlyLocal ? "AND site_id = crsql_site_id() " : string.Empty;

        await using var command = await CommandAsync(
            $"SELECT {Columns} FROM crsql_changes WHERE db_version > $since {mine}ORDER BY db_version, seq;",
            cancellationToken).ConfigureAwait(false);

        command.Parameters.AddWithValue("$since", sinceDbVersion);

        var changes = new List<CrdtChange>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            changes.Add(new CrdtChange(
                reader.GetString(0),
                (byte[])reader.GetValue(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetValue(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                (byte[])reader.GetValue(6),
                reader.GetInt64(7),
                reader.GetInt64(8)));
        }

        return changes;
    }

    private async Task<SqliteCommand> CommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }
}
