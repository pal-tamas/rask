using System.Globalization;
using Microsoft.Data.Sqlite;
using Rask.Batteries;

namespace Rask.Logging;

/// <summary>
///     The shape of the file store's <c>logs.db</c>: the <c>RaskLog</c> table keyed by a <see cref="Guid" />, its
///     indexes, and the full-text index beside it — created on a new file and brought forward on an old one.
/// </summary>
/// <remarks>
///     <para>
///         <c>Id</c> is the entry's key: a version-7 <see cref="Guid" /> Rask makes before the insert, stored as
///         uppercase text — the form EF Core's SQLite provider writes a <see cref="Guid" /> in, so an id reads the same
///         in both stores and in a <c>sqlite3</c> shell. The table is <c>WITHOUT ROWID</c>: the key is the table's own
///         b-tree, and there is no rowid for anything to lean on.
///     </para>
///     <para>
///         <c>Sequence</c> is the entry's place in the write order, and the number the full-text index addresses it by —
///         FTS5 addresses a row by an integer only. It is never handed out and never part of a record, and it is an
///         ordinary column, so <c>VACUUM</c> leaves it alone and the index stays bound to its rows. A page is ordered by
///         it, not by <c>Id</c>, although the two agree — both are made in the batch's loop under one write lock —
///         because a search's matches come back as these numbers: walked newest first, a page reads its own fifty rows,
///         where ordering by <c>Id</c> would sort every match first (three to six times slower for a word most entries
///         hold, measured on 100k entries). Its index carries <c>Level</c> as well, so counting the matches of a search
///         filtered by level never leaves it. Unique by construction — the writer takes the next numbers under the write
///         lock — rather than by a second index on every insert. On a re-keyed file the old rowid becomes it, so old
///         entries keep their old order.
///     </para>
///     <para>
///         There is no migration history here — this database is framework-owned and deliberately outside the app's — so
///         what to bring forward is read off the schema itself.
///     </para>
/// </remarks>
internal static class SqliteLogSchema
{
    private const string Columns = """
        (
            Id        TEXT    NOT NULL PRIMARY KEY,
            Sequence  INTEGER NOT NULL,
            Timestamp TEXT    NOT NULL,
            Level     INTEGER NOT NULL,
            Category  TEXT    NOT NULL,
            EventId   INTEGER NOT NULL,
            Message   TEXT    NOT NULL,
            Exception TEXT,
            Scopes    TEXT
        ) WITHOUT ROWID;

        """;

    private const string Indexes = """
        CREATE INDEX IF NOT EXISTS IX_RaskLog_Sequence_Level ON RaskLog (Sequence, Level);
        CREATE INDEX IF NOT EXISTS IX_RaskLog_Timestamp ON RaskLog (Timestamp);
        CREATE INDEX IF NOT EXISTS IX_RaskLog_Level_Sequence ON RaskLog (Level, Sequence DESC);
        CREATE INDEX IF NOT EXISTS IX_RaskLog_Category_Sequence ON RaskLog (Category, Sequence DESC);

        """;

    // The id an entry of an integer-keyed file is given when the file is re-keyed: OrderedGuid.Of over its time and
    // old number, the formula that file's entries were shown under, so an id seen before stays the same.
    private const string OldIdFunction = "rask_log_id_of";

    /// <summary>Creates the table and its indexes, re-keying a file an earlier release wrote.</summary>
    /// <remarks>
    ///     One transaction: a file is re-keyed, created and indexed together, or not at all. Whether to backfill the
    ///     search index is decided by whether it exists, so one created and then interrupted before its rebuild would
    ///     leave every older entry unsearchable for good; a re-key interrupted halfway would leave two tables.
    /// </remarks>
    internal static async Task Ensure(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            if (await HasIntegerKey(connection, transaction, cancellationToken).ConfigureAwait(false))
            {
                await Rekey(connection, transaction, cancellationToken).ConfigureAwait(false);
            }

            await Execute(
                connection,
                transaction,
                "CREATE TABLE IF NOT EXISTS RaskLog " + Columns + Indexes,
                cancellationToken).ConfigureAwait(false);
            await EnsureSearchIndex(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Whether the file was written by a release that keyed an entry by its integer rowid.</summary>
    private static async Task<bool> HasIntegerKey(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken) =>
        await Count(
            connection,
            transaction,
            "SELECT COUNT(*) FROM pragma_table_info('RaskLog') WHERE name = 'Id' AND upper(type) = 'INTEGER';",
            cancellationToken).ConfigureAwait(false) > 0;

    /// <summary>
    ///     Rebuilds an integer-keyed file into the <see cref="Guid" />-keyed shape. Every entry keeps the id it was shown
    ///     with (<see cref="OrderedGuid.Of" /> over its time and old number) and keeps its old number as its place in the write order.
    /// </summary>
    /// <remarks>
    ///     The old search index is dropped with its triggers — it was bound to the old table — and
    ///     <see cref="EnsureSearchIndex" /> rebuilds it in the same transaction. A file from before scopes gets that column
    ///     first, so the copy reads one shape.
    /// </remarks>
    private static async Task Rekey(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await AddColumnIfMissing(connection, transaction, "Scopes", "TEXT", cancellationToken).ConfigureAwait(false);

        connection.CreateFunction(
            OldIdFunction,
            static (string timestamp, long number) =>
                SqliteLogStore.FormatId(OrderedGuid.Of(SqliteLogStore.ParseTimestamp(timestamp), number)),
            isDeterministic: true);

        await Execute(
            connection,
            transaction,
            """
            DROP TRIGGER IF EXISTS TR_RaskLog_Search_Insert;
            DROP TRIGGER IF EXISTS TR_RaskLog_Search_Delete;
            DROP TRIGGER IF EXISTS TR_RaskLog_Search_Update;
            DROP TABLE IF EXISTS RaskLogSearch;
            CREATE TABLE RaskLogRekeyed
            """ + Columns + $"""
            INSERT INTO RaskLogRekeyed (Id, Sequence, Timestamp, Level, Category, EventId, Message, Exception, Scopes)
            SELECT {OldIdFunction}(Timestamp, Id), Id, Timestamp, Level, Category, EventId, Message, Exception, Scopes
            FROM RaskLog ORDER BY Id;
            DROP TABLE RaskLog;
            ALTER TABLE RaskLogRekeyed RENAME TO RaskLog;
            """,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     The substring index behind <see cref="LogQuery.Search" /> (#1111): an FTS5 table with the <c>trigram</c>
    ///     tokenizer over Message and Exception, kept current by triggers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Log search wants SUBSTRINGS — an id, a path, half an exception type — not words, so the tokenizer is
    ///         <c>trigram</c> rather than FTS5's default: a quoted phrase then matches anywhere inside the text, and the
    ///         match is served from the index instead of a <c>LIKE '%…%'</c> scan of every row retention keeps.
    ///     </para>
    ///     <para>
    ///         External content (<c>content='RaskLog'</c>, addressed by <c>Sequence</c>), so the text is stored once. The
    ///         triggers keep the index in step with every insert and with retention's deletes; a store that predates the
    ///         index, or was just re-keyed, is backfilled once with <c>rebuild</c>. Written out here rather than shared
    ///         with Rask.SQLite.EntityFrameworkCore's full-text DDL, which is built on EF's migration pipeline over an EF
    ///         model — this store is raw ADO.NET on a framework-owned file, and five statements do not justify pulling
    ///         that pipeline in.
    ///     </para>
    /// </remarks>
    private static async Task EnsureSearchIndex(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var existed = await Count(
            connection,
            transaction,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'RaskLogSearch';",
            cancellationToken).ConfigureAwait(false) > 0;

        var sql = """
            CREATE VIRTUAL TABLE IF NOT EXISTS RaskLogSearch USING fts5(
                Message, Exception, content='RaskLog', content_rowid='Sequence', tokenize='trigram');
            CREATE TRIGGER IF NOT EXISTS TR_RaskLog_Search_Insert AFTER INSERT ON RaskLog BEGIN
                INSERT INTO RaskLogSearch (rowid, Message, Exception) VALUES (new.Sequence, new.Message, new.Exception);
            END;
            CREATE TRIGGER IF NOT EXISTS TR_RaskLog_Search_Delete AFTER DELETE ON RaskLog BEGIN
                INSERT INTO RaskLogSearch (RaskLogSearch, rowid, Message, Exception)
                VALUES ('delete', old.Sequence, old.Message, old.Exception);
            END;
            CREATE TRIGGER IF NOT EXISTS TR_RaskLog_Search_Update AFTER UPDATE OF Sequence, Message, Exception ON RaskLog BEGIN
                INSERT INTO RaskLogSearch (RaskLogSearch, rowid, Message, Exception)
                VALUES ('delete', old.Sequence, old.Message, old.Exception);
                INSERT INTO RaskLogSearch (rowid, Message, Exception) VALUES (new.Sequence, new.Message, new.Exception);
            END;

            """;
        if (!existed)
        {
            // The rows a store written before the index already holds. Once, while the store's schema gate is held.
            sql += "INSERT INTO RaskLogSearch (RaskLogSearch) VALUES ('rebuild');";
        }

        await Execute(connection, transaction, sql, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Adds a column when the table predates it. Idempotent, and cheap — <c>PRAGMA table_info</c> reads the schema
    ///     SQLite already has in memory.
    /// </summary>
    private static async Task AddColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string column,
        string type,
        CancellationToken cancellationToken)
    {
        var probe = connection.CreateCommand();
        probe.Transaction = transaction;
        await using (probe.ConfigureAwait(false))
        {
            probe.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RaskLog') WHERE name = $name;";
            probe.Parameters.AddWithValue("$name", column);
            var present = Convert.ToInt64(
                await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (present > 0)
            {
                return;
            }
        }

        // Interpolated rather than parameterised because an identifier cannot be a parameter. Both values are
        // compile-time constants from this file, never user input.
        await Execute(connection, transaction, $"ALTER TABLE RaskLog ADD COLUMN {column} {type};", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<long> Count(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            return Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }
    }

    private static async Task Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        await using (command.ConfigureAwait(false))
        {
#pragma warning disable S2077 // the SQL is built from constant fragments of this file; nothing in it comes from outside
            command.CommandText = sql;
#pragma warning restore S2077
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
