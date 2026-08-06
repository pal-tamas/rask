using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Rask.SQLite;

namespace Rask.Logging;

/// <summary>
/// The <see cref="ILogStore"/> implementation: an append-only table in a SQLite file of its own.
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
internal sealed class SqliteLogStore : ILogStore
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

    public async Task AppendAsync(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return;
        }

        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.ExecuteInImmediateTransactionAsync(
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
                            category.Value = record.Category;
                            eventId.Value = record.EventId;
                            message.Value = record.Message;
                            exception.Value = (object?)record.Exception ?? DBNull.Value;
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

    public async Task<LogPage> QueryAsync(LogQuery query, CancellationToken cancellationToken = default)
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

    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken cancellationToken = default)
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

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await CountAsync(connection, string.Empty, [], cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> PurgeAsync(
        TimeSpan retention,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
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

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.ExecuteInImmediateTransactionAsync(
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
            }

            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
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
            var deleted = await connection.ExecuteInImmediateTransactionAsync(
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
            Add(
                @"(Message LIKE $search ESCAPE '\' OR Exception LIKE $search ESCAPE '\')",
                "$search",
                SqliteType.Text,
                $"%{EscapeLike(query.Search)}%");
        }

        if (!string.IsNullOrWhiteSpace(query.ScopeKey))
        {
            // json_extract rather than a LIKE over the raw column: a LIKE for "RequestId" would also match
            // an entry whose *message* merely mentioned it, and a value search would match a prefix of a
            // different id. SQLite ships JSON1 in every build Microsoft.Data.Sqlite bundles.
            //
            // The key is concatenated into the JSON path as a PARAMETER, not into the SQL, so a key
            // containing quotes cannot change the shape of the statement.
            var clause = string.IsNullOrWhiteSpace(query.ScopeValue)
                ? "json_extract(Scopes, '$.' || $scopeKey) IS NOT NULL"
                : "json_extract(Scopes, '$.' || $scopeKey) = $scopeValue";

            Add(clause, "$scopeKey", SqliteType.Text, query.ScopeKey);

            if (!string.IsNullOrWhiteSpace(query.ScopeValue))
            {
                filters.Add(new SqliteParameter("$scopeValue", SqliteType.Text) { Value = query.ScopeValue });
            }
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
