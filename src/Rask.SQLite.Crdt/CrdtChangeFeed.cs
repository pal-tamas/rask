using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
public sealed class CrdtChangeFeed(DbContext context)
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
    public async Task<long> GetDbVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var command = await CommandAsync("SELECT crsql_db_version();", cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    ///     Every change this replica has that a peer has not seen — those with a version above
    ///     <paramref name="sinceDbVersion" />, oldest first.
    /// </summary>
    /// <remarks>
    ///     Asking by version rather than by time is what keeps a sync proportional to what changed instead
    ///     of to how long the database has existed.
    /// </remarks>
    public async Task<IReadOnlyList<CrdtChange>> ReadChangesAsync(
        long sinceDbVersion = -1, CancellationToken cancellationToken = default)
    {
        await using var command = await CommandAsync(
            $"SELECT {Columns} FROM crsql_changes WHERE db_version > $since ORDER BY db_version, seq;",
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

    /// <summary>
    ///     Merges changes from a peer. Re-applying something already seen changes nothing, so a caller
    ///     never has to work out what is new.
    /// </summary>
    public async Task ApplyChangesAsync(
        IEnumerable<CrdtChange> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        foreach (var change in changes)
        {
            await using var command = await CommandAsync(
                $"INSERT INTO crsql_changes ({Columns}) " +
                "VALUES ($table, $pk, $cid, $val, $cv, $dv, $site, $cl, $seq);",
                cancellationToken).ConfigureAwait(false);

            command.Parameters.AddWithValue("$table", change.Table);
            command.Parameters.AddWithValue("$pk", change.PrimaryKey);
            command.Parameters.AddWithValue("$cid", change.ColumnName);
            command.Parameters.AddWithValue("$val", change.Value ?? DBNull.Value);
            command.Parameters.AddWithValue("$cv", change.ColumnVersion);
            command.Parameters.AddWithValue("$dv", change.DbVersion);
            command.Parameters.AddWithValue("$site", change.SiteId);
            command.Parameters.AddWithValue("$cl", change.CausalLength);
            command.Parameters.AddWithValue("$seq", change.Sequence);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SqliteCommand> CommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}
