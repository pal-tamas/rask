using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Rask.SQLite;

namespace Rask.Logging;

/// <summary>
/// The <see cref="ILogs"/> implementation: an append-only table in a SQLite file of its own.
/// <para>
/// A file of its own, rather than the application's <c>DbContext</c> the other pillars map onto, for three
/// reasons. Log lines arrive at machine rates, and routing them through the app's context would put a
/// high-frequency writer on the same single write lock the request path already contends for. The most
/// valuable line is the one written <i>while a transaction is failing</i> — on the app's context that line
/// rolls back with the failure, losing exactly what the store exists to keep. And a framework-owned
/// append-only table has no business in the application's migration history, which is why the schema is
/// created here rather than shipped as a migration.
/// </para>
/// <para>
/// The trade-off is that <c>rask db backup</c> and Litestream cover the application database, not this one.
/// That is deliberate: logs are expendable and high-churn, and keeping them out of that file keeps snapshots
/// and WAL replication cheap.
/// </para>
/// </summary>
internal sealed class SqliteLogStore : ILogs
{
    private const string InsertSql = """
        INSERT INTO RaskLog (Timestamp, Level, Category, EventId, Message, Exception, Scopes)
        VALUES ($timestamp, $level, $category, $eventId, $message, $exception, $scopes);
        """;

    // Deletes are paged so a single unbounded DELETE never holds SQLite's write lock for the length of a
    // whole sweep — the same reasoning (and page size) as the outbox's retention purge.
    private const int PurgePageSize = 1000;

    private readonly string _connectionString;
    private readonly RaskLoggingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);

    private volatile bool _schemaReady;

    public SqliteLogStore(string connectionString, RaskLoggingOptions options, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _connectionString = connectionString;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task Append(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return;
        }

        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.InImmediateTransactionAsync(
                _options.BusyRetry,
                async (c, token) =>
                {
                    // One command prepared once and re-bound per row: the batch is the whole point, and
                    // re-creating the command per entry would throw away the prepared statement each time.
                    var command = c.CreateCommand();
                    await using (command.ConfigureAwait(false))
                    {
                        command.CommandText = InsertSql;
                        var timestamp = command.Parameters.Add("$timestamp", SqliteType.Text);
                        var level = command.Parameters.Add("$level", SqliteType.Integer);
                        var category = command.Parameters.Add("$category", SqliteType.Text);
                        var eventId = command.Parameters.Add("$eventId", SqliteType.Integer);
                        var message = command.Parameters.Add("$message", SqliteType.Text);
                        var exception = command.Parameters.Add("$exception", SqliteType.Text);
                        var scopes = command.Parameters.Add("$scopes", SqliteType.Text);

                        foreach (var record in records)
                        {
                            timestamp.Value = FormatTimestamp(record.Timestamp);
                            level.Value = (int)record.Level;
                            // NUL as U+FFFD, the same as the application-database store (see LogText), so a
                            // line reads back identically whichever store keeps it.
                            category.Value = LogText.WithoutNul(record.Category);
                            eventId.Value = record.EventId;
                            message.Value = LogText.WithoutNul(record.Message);
                            exception.Value = record.Exception is null
                                ? DBNull.Value
                                : LogText.WithoutNul(record.Exception);
                            // Encoded here, on the writer's thread, rather than at the log call — the call
                            // site only pays for the flattened snapshot (see LogScopes).
                            scopes.Value = (object?)LogScopeJson.Encode(record.Scopes) ?? DBNull.Value;
                            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        }
                    }
                },
                _timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<LogPage> Search(LogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 1000);

        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var where = new StringBuilder();
            var filters = BuildFilters(query, where);

            var total = await CountAsync(connection, where.ToString(), filters, cancellationToken)
                .ConfigureAwait(false);
            if (total == 0)
            {
                return LogPage.Empty(page, pageSize);
            }

            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                // Ordered by Id, not Timestamp: the id is monotonic per insert, so it orders entries logged
                // inside the same clock tick deterministically, and paging over a stable order is the only
                // way a page boundary doesn't drop or repeat a row.
                command.CommandText =
                    $"SELECT Id, Timestamp, Level, Category, EventId, Message, Exception, Scopes FROM RaskLog{where} "
                    + "ORDER BY Id DESC LIMIT $limit OFFSET $offset;";
                Bind(command, filters);
                command.Parameters.AddWithValue("$limit", pageSize);
                command.Parameters.AddWithValue("$offset", (long)(page - 1) * pageSize);

                var entries = new List<LogRecord>(pageSize);
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        entries.Add(new LogRecord(
                            reader.GetInt64(0),
                            ParseTimestamp(reader.GetString(1)),
                            (LogLevel)reader.GetInt32(2),
                            reader.GetString(3),
                            reader.GetInt32(4),
                            reader.GetString(5),
                            reader.IsDBNull(6) ? null : reader.GetString(6),
                            reader.IsDBNull(7) ? null : LogScopeJson.Decode(reader.GetString(7))));
                    }
                }

                return new LogPage(entries, total, page, pageSize);
            }
        }
    }

    public async Task<IReadOnlyList<string>> Categories(CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = "SELECT DISTINCT Category FROM RaskLog ORDER BY Category;";

                var categories = new List<string>();
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        categories.Add(reader.GetString(0));
                    }
                }

                return categories;
            }
        }
    }

    public async Task<long> Count(CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await CountAsync(connection, string.Empty, [], cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> Trim(
        TimeSpan? olderThan,
        int? keepNewest,
        CancellationToken cancellationToken = default)
    {
        // The nullable pair is the interface's; the body below still reasons in the old "zero means skip"
        // terms, so it is translated once, here, rather than at every comparison.
        var retention = olderThan ?? TimeSpan.Zero;
        var maxRows = keepNewest ?? 0;
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var removed = 0;

            if (retention > TimeSpan.Zero)
            {
                var cutoff = FormatTimestamp(_timeProvider.GetUtcNow() - retention);
                removed += await PurgePagesAsync(
                    connection,
                    "SELECT Id FROM RaskLog WHERE Timestamp < $cutoff ORDER BY Id LIMIT $page",
                    command => command.Parameters.AddWithValue("$cutoff", cutoff),
                    cancellationToken).ConfigureAwait(false);
            }

            if (maxRows > 0)
            {
                // The threshold is resolved once per sweep rather than per page: it is the id of the
                // oldest row that is still allowed to survive, so it doesn't move as pages are deleted.
                // Rows arriving mid-sweep are simply left for the next one — the cap is a backstop, not a
                // per-insert invariant.
                var threshold = await ScalarAsync(
                    connection,
                    "SELECT Id FROM RaskLog ORDER BY Id DESC LIMIT 1 OFFSET $max;",
                    command => command.Parameters.AddWithValue("$max", maxRows),
                    cancellationToken).ConfigureAwait(false);

                if (threshold is { } oldestKept)
                {
                    removed += await PurgePagesAsync(
                        connection,
                        "SELECT Id FROM RaskLog WHERE Id <= $threshold ORDER BY Id LIMIT $page",
                        command => command.Parameters.AddWithValue("$threshold", oldestKept),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            return removed;
        }
    }

    public async Task Clear(CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.InImmediateTransactionAsync(
                _options.BusyRetry,
                async (c, token) =>
                {
                    var command = c.CreateCommand();
                    await using (command.ConfigureAwait(false))
                    {
                        command.CommandText = "DELETE FROM RaskLog;";
                        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                },
                _timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens a connection with the production pragmas applied. Connections are pooled by connection string,
    /// so this is a lease rather than a file open — but the per-connection pragmas must be re-applied on
    /// every lease, which is why they are set here and not once at startup.
    /// </summary>
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        return await OpenRawAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenRawAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await SqlitePragmas.ApplyAsync(connection, _options.Pragmas, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates the table and its indexes, once per store. Gated rather than raced: the writer and a
    /// dashboard reader can both be the first to touch the file.
    /// </summary>
    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            var connection = await OpenRawAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    // Id is a plain INTEGER PRIMARY KEY (a rowid alias), not AUTOINCREMENT: AUTOINCREMENT
                    // costs a sqlite_sequence write on every insert, and the id reuse it prevents cannot
                    // happen here — retention only ever deletes the oldest rows, so the maximum never drops.
                    command.CommandText = """
                        CREATE TABLE IF NOT EXISTS RaskLog (
                            Id        INTEGER PRIMARY KEY,
                            Timestamp TEXT    NOT NULL,
                            Level     INTEGER NOT NULL,
                            Category  TEXT    NOT NULL,
                            EventId   INTEGER NOT NULL,
                            Message   TEXT    NOT NULL,
                            Exception TEXT,
                            Scopes    TEXT
                        );
                        CREATE INDEX IF NOT EXISTS IX_RaskLog_Timestamp ON RaskLog (Timestamp);
                        CREATE INDEX IF NOT EXISTS IX_RaskLog_Level_Id ON RaskLog (Level, Id DESC);
                        CREATE INDEX IF NOT EXISTS IX_RaskLog_Category_Id ON RaskLog (Category, Id DESC);
                        """;
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                // Scopes arrived after the first release of this package, and CREATE TABLE IF NOT EXISTS
                // does nothing to a table that already exists — so a store created by that release would
                // otherwise fail every insert with "no such column". There is no migration history here
                // (this database is framework-owned and deliberately outside the app's), so the check is
                // the schema itself.
                await AddColumnIfMissingAsync(connection, "Scopes", "TEXT", cancellationToken)
                    .ConfigureAwait(false);

                await EnsureSearchIndexAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    /// <summary>
    ///     The substring index behind <see cref="LogQuery.Search" /> (#1111): an FTS5 table with the <c>trigram</c>
    ///     tokenizer over Message and Exception, kept current by triggers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Log search wants SUBSTRINGS — an id, a path, half an exception type — not words, so the tokenizer is
    ///     <c>trigram</c> rather than FTS5's default: a quoted phrase then matches anywhere inside the text, and the
    ///     match is served from the index instead of a <c>LIKE '%…%'</c> scan of every row retention keeps.
    ///     </para>
    ///     <para>
    ///     External content (<c>content='RaskLog'</c>), so the text is stored once. The triggers keep the index in
    ///     step with every insert and with retention's deletes; a store that predates the index is backfilled once
    ///     with <c>rebuild</c>. Written out here rather than shared with Rask.SQLite.EntityFrameworkCore's
    ///     full-text DDL, which is built on EF's migration pipeline over an EF model — this store is raw ADO.NET on
    ///     a framework-owned file, and five statements do not justify pulling that pipeline in.
    ///     </para>
    /// </remarks>
    private static async Task EnsureSearchIndexAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // One transaction: whether to backfill is decided by whether the index exists, so an index created and then
        // interrupted before its rebuild would never be backfilled — every entry older than the upgrade unsearchable
        // for good. Created and rebuilt together, or neither.
        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await CreateSearchIndexAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CreateSearchIndexAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var probe = connection.CreateCommand();
        probe.Transaction = transaction;
        bool existed;
        await using (probe.ConfigureAwait(false))
        {
            probe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'RaskLogSearch';";
            existed = Convert.ToInt64(
                await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture) > 0;
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = """
                CREATE VIRTUAL TABLE IF NOT EXISTS RaskLogSearch USING fts5(
                    Message, Exception, content='RaskLog', content_rowid='Id', tokenize='trigram');
                CREATE TRIGGER IF NOT EXISTS TR_RaskLog_Search_Insert AFTER INSERT ON RaskLog BEGIN
                    INSERT INTO RaskLogSearch (rowid, Message, Exception) VALUES (new.Id, new.Message, new.Exception);
                END;
                CREATE TRIGGER IF NOT EXISTS TR_RaskLog_Search_Delete AFTER DELETE ON RaskLog BEGIN
                    INSERT INTO RaskLogSearch (RaskLogSearch, rowid, Message, Exception)
                    VALUES ('delete', old.Id, old.Message, old.Exception);
                END;
                CREATE TRIGGER IF NOT EXISTS TR_RaskLog_Search_Update AFTER UPDATE OF Message, Exception ON RaskLog BEGIN
                    INSERT INTO RaskLogSearch (RaskLogSearch, rowid, Message, Exception)
                    VALUES ('delete', old.Id, old.Message, old.Exception);
                    INSERT INTO RaskLogSearch (rowid, Message, Exception) VALUES (new.Id, new.Message, new.Exception);
                END;
                """;
            if (!existed)
            {
                // The rows a store written before the index already holds. Once, while the schema gate is held.
                command.CommandText += "INSERT INTO RaskLogSearch (RaskLogSearch) VALUES ('rebuild');";
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Adds a column when the table predates it. Idempotent, and cheap enough to run on every schema
    ///     check — <c>PRAGMA table_info</c> reads the schema SQLite already has in memory.
    /// </summary>
    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        string column,
        string type,
        CancellationToken cancellationToken)
    {
        var probe = connection.CreateCommand();
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

        var alter = connection.CreateCommand();
        await using (alter.ConfigureAwait(false))
        {
            // Interpolated rather than parameterised because an identifier cannot be a parameter. Both
            // values are compile-time constants from this file, never user input.
            alter.CommandText = $"ALTER TABLE RaskLog ADD COLUMN {column} {type};";
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Deletes matching rows a page at a time, looping until the sweep is drained.</summary>
    private async Task<int> PurgePagesAsync(
        SqliteConnection connection,
        string selectIds,
        Action<SqliteCommand> bind,
        CancellationToken cancellationToken)
    {
        var removed = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var deleted = await connection.InImmediateTransactionAsync(
                _options.BusyRetry,
                async (c, token) =>
                {
                    var command = c.CreateCommand();
                    await using (command.ConfigureAwait(false))
                    {
                        command.CommandText = $"DELETE FROM RaskLog WHERE Id IN ({selectIds});";
                        command.Parameters.AddWithValue("$page", PurgePageSize);
                        bind(command);
                        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                },
                _timeProvider,
                cancellationToken).ConfigureAwait(false);

            removed += deleted;
            if (deleted < PurgePageSize)
            {
                break;
            }
        }

        return removed;
    }

    private static async Task<long> CountAsync(
        SqliteConnection connection,
        string where,
        IReadOnlyList<SqliteParameter> filters,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = $"SELECT COUNT(*) FROM RaskLog{where};";
            Bind(command, filters);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is long count ? count : 0;
        }
    }

    private static async Task<long?> ScalarAsync(
        SqliteConnection connection,
        string sql,
        Action<SqliteCommand> bind,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            bind(command);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is long value ? value : null;
        }
    }

    /// <summary>
    /// Appends the <c>WHERE</c> clause for <paramref name="query"/> to <paramref name="where"/> and returns
    /// the parameters it references. Every value the caller supplied travels as a parameter — the clause
    /// itself is assembled only from constant fragments.
    /// </summary>
    private static List<SqliteParameter> BuildFilters(LogQuery query, StringBuilder where)
    {
        var filters = new List<SqliteParameter>();

        void Add(string clause, string name, SqliteType type, object value)
        {
            where.Append(filters.Count == 0 ? " WHERE " : " AND ").Append(clause);
            filters.Add(new SqliteParameter(name, type) { Value = value });
        }

        if (query.MinimumLevel is { } minimumLevel)
        {
            Add("Level >= $level", "$level", SqliteType.Integer, (int)minimumLevel);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            Add(
                @"Category LIKE $category ESCAPE '\'",
                "$category",
                SqliteType.Text,
                $"%{EscapeLike(query.Category)}%");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Characters, not UTF-16 units: the trigram tokenizer counts code points, so "😀a" is two of them — too
            // few for a trigram — although its string length is three.
            if (query.Search.EnumerateRunes().Count() >= 3)
            {
                // Served by the trigram index: a quoted phrase is a case-insensitive substring match in either
                // column. Inside the quotes every character is literal, a doubled quote included — so `%` and `_`
                // mean themselves, as they did escaped in LIKE.
                Add(
                    "Id IN (SELECT rowid FROM RaskLogSearch WHERE RaskLogSearch MATCH $search)",
                    "$search",
                    SqliteType.Text,
                    "\"" + query.Search.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"");
            }
            else
            {
                // A trigram index has nothing to look up for fewer than three characters, so a one- or two-character
                // search is the scan it always was.
                Add(
                    @"(Message LIKE $search ESCAPE '\' OR Exception LIKE $search ESCAPE '\')",
                    "$search",
                    SqliteType.Text,
                    $"%{EscapeLike(query.Search)}%");
            }
        }

        if (!string.IsNullOrWhiteSpace(query.ScopeKey))
        {
            // The encoded "key":"value" text, found with instr — exact, and the same match the application-database
            // store makes (see LogScopeJson.Fragment). It replaced json_extract(Scopes, '$.' || key), which read the
            // key as a JSON *path*: a dotted or bracketed key (user.id, items[0]) was a nested path that matched
            // nothing, and a key starting with a double quote was no valid path at all, so the query threw.
            Add(
                "instr(Scopes, $scope) > 0",
                "$scope",
                SqliteType.Text,
                LogScopeJson.Fragment(
                    query.ScopeKey,
                    string.IsNullOrWhiteSpace(query.ScopeValue) ? null : query.ScopeValue));
        }

        if (query.From is { } from)
        {
            Add("Timestamp >= $from", "$from", SqliteType.Text, FormatTimestamp(from));
        }

        if (query.To is { } to)
        {
            Add("Timestamp <= $to", "$to", SqliteType.Text, FormatTimestamp(to));
        }

        return filters;
    }

    private static void Bind(SqliteCommand command, IReadOnlyList<SqliteParameter> filters)
    {
        foreach (var filter in filters)
        {
            // Cloned rather than reused: one parameter instance cannot belong to two commands, and the
            // count and page queries share the same filter list.
            command.Parameters.Add(new SqliteParameter(filter.ParameterName, filter.SqliteType)
            {
                Value = filter.Value,
            });
        }
    }

    /// <summary>Escapes the LIKE wildcards in user-supplied filter text, so a <c>%</c> matches a literal <c>%</c>.</summary>
    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    /// <summary>
    /// Round-trip UTC text. Fixed-width and lexicographically ordered, so a range filter is a plain string
    /// comparison and the file stays readable in a <c>sqlite3</c> shell — which matters for a table whose
    /// whole job is being read during an incident.
    /// </summary>
    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        new(DateTime.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
