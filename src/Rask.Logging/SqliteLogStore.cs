using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Rask.Batteries;
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
internal sealed class SqliteLogStore : ILogs, IDisposable
{
    private const string InsertSql = """
        INSERT INTO RaskLog (Id, Sequence, Timestamp, Level, Category, EventId, Message, Exception, Scopes)
        VALUES ($id, $sequence, $timestamp, $level, $category, $eventId, $message, $exception, $scopes);
        """;

    // Deletes are paged so a single unbounded DELETE never holds SQLite's write lock for the length of a
    // whole sweep — the same reasoning (and page size) as the outbox's retention purge.
    private const int PurgePageSize = 1000;

    private readonly string _connectionString;
    private readonly LogsOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);

    private volatile bool _schemaReady;

    public SqliteLogStore(string connectionString, LogsOptions options, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _connectionString = connectionString;
        _options = options;
        _timeProvider = timeProvider;
    }

    public void Dispose() => _schemaGate.Dispose();

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
            await connection.InImmediateTransaction(
                _options.BusyRetry,
                async (c, token) =>
                {
                    // Read once per batch, inside the write lock the transaction holds, so the places the batch takes
                    // are its own; its index makes MAX one descent rather than a scan.
                    var last = await ScalarAsync(c, "SELECT MAX(Sequence) FROM RaskLog;", static _ => { }, token)
                        .ConfigureAwait(false) ?? 0;

                    // One command prepared once and re-bound per row: the batch is the whole point, and
                    // re-creating the command per entry would throw away the prepared statement each time.
                    var command = c.CreateCommand();
                    await using (command.ConfigureAwait(false))
                    {
                        command.CommandText = InsertSql;
                        var id = command.Parameters.Add("$id", SqliteType.Text);
                        var sequence = command.Parameters.Add("$sequence", SqliteType.Integer);
                        var timestamp = command.Parameters.Add("$timestamp", SqliteType.Text);
                        var level = command.Parameters.Add("$level", SqliteType.Integer);
                        var category = command.Parameters.Add("$category", SqliteType.Text);
                        var eventId = command.Parameters.Add("$eventId", SqliteType.Integer);
                        var message = command.Parameters.Add("$message", SqliteType.Text);
                        var exception = command.Parameters.Add("$exception", SqliteType.Text);
                        var scopes = command.Parameters.Add("$scopes", SqliteType.Text);

                        foreach (var record in records)
                        {
                            // Made here, before the insert, as the application-database store makes its own.
                            id.Value = FormatId(OrderedGuid.New());
                            sequence.Value = ++last;
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

            var total = await CountMatchesAsync(connection, query, where.ToString(), filters, cancellationToken)
                .ConfigureAwait(false);
            if (total == 0)
            {
                return LogPage.Empty(page, pageSize);
            }

            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                // Ordered by the write order, not Timestamp: it orders entries logged inside the same clock tick
                // deterministically, and paging over a stable order is the only way a page boundary doesn't drop or
                // repeat a row. It is the order of the ids too — each is made in the batch's loop under the same write
                // lock — and it is what a search is matched by, so the matches are walked newest first and a page
                // reads only its own rows instead of sorting every match (see SqliteLogSchema).
#pragma warning disable S2077 // the SQL is built from constant fragments; every value is a bound parameter
                command.CommandText =
                    $"SELECT Id, Timestamp, Level, Category, EventId, Message, Exception, Scopes FROM RaskLog{where} "
                    + "ORDER BY Sequence DESC LIMIT $limit OFFSET $offset;";
#pragma warning restore S2077
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
                            Guid.Parse(reader.GetString(0)),
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
            return await CountAsync(connection, string.Empty, "RaskLog", [], cancellationToken).ConfigureAwait(false);
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
                    "SELECT Sequence FROM RaskLog WHERE Timestamp < $cutoff ORDER BY Sequence LIMIT $page",
                    command => command.Parameters.AddWithValue("$cutoff", cutoff),
                    cancellationToken).ConfigureAwait(false);
            }

            if (maxRows > 0)
            {
                // The threshold is resolved once per sweep rather than per page: it is the place of the
                // oldest row that is still allowed to survive, so it doesn't move as pages are deleted.
                // Rows arriving mid-sweep are simply left for the next one — the cap is a backstop, not a
                // per-insert invariant.
                var threshold = await ScalarAsync(
                    connection,
                    "SELECT Sequence FROM RaskLog ORDER BY Sequence DESC LIMIT 1 OFFSET $max;",
                    command => command.Parameters.AddWithValue("$max", maxRows),
                    cancellationToken).ConfigureAwait(false);

                if (threshold is { } oldestKept)
                {
                    removed += await PurgePagesAsync(
                        connection,
                        "SELECT Sequence FROM RaskLog WHERE Sequence <= $threshold ORDER BY Sequence LIMIT $page",
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
            await connection.InImmediateTransaction(
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
    /// Brings the file to the current shape (<see cref="SqliteLogSchema"/>), once per store. Gated rather than
    /// raced: the writer and a dashboard reader can both be the first to touch the file.
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
                await SqliteLogSchema.Ensure(connection, cancellationToken).ConfigureAwait(false);
            }

            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
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
            var deleted = await connection.InImmediateTransaction(
                _options.BusyRetry,
                async (c, token) =>
                {
                    var command = c.CreateCommand();
                    await using (command.ConfigureAwait(false))
                    {
#pragma warning disable S2077 // the SQL is built from constant fragments; every value is a bound parameter
                        command.CommandText = $"DELETE FROM RaskLog WHERE Sequence IN ({selectIds});";
#pragma warning restore S2077
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

    /// <summary>
    /// The entries <paramref name="query"/> matches. A search and nothing else is counted in the index alone: every
    /// row has exactly one entry there, so the count is the same, without looking each match up in the table.
    /// </summary>
    private static Task<long> CountMatchesAsync(
        SqliteConnection connection,
        LogQuery query,
        string where,
        IReadOnlyList<SqliteParameter> filters,
        CancellationToken cancellationToken) =>
        filters is [{ ParameterName: "$search" }] && IsIndexed(query.Search!)
            ? CountAsync(connection, " WHERE RaskLogSearch MATCH $search", "RaskLogSearch", filters, cancellationToken)
            : CountAsync(connection, where, "RaskLog", filters, cancellationToken);

    private static async Task<long> CountAsync(
        SqliteConnection connection,
        string where,
        string table,
        IReadOnlyList<SqliteParameter> filters,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
#pragma warning disable S2077 // the SQL is built from constant fragments; every value is a bound parameter
            command.CommandText = $"SELECT COUNT(*) FROM {table}{where};";
#pragma warning restore S2077
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
            var (clause, value) = SearchFilter(query.Search);
            Add(clause, "$search", SqliteType.Text, value);
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

    /// <summary>Whether the trigram index can look <paramref name="search"/> up: three characters or more.</summary>
    /// <remarks>
    /// Characters, not UTF-16 units: the trigram tokenizer counts code points, so "😀a" is two of them — too few for
    /// a trigram — although its string length is three.
    /// </remarks>
    private static bool IsIndexed(string search) => search.EnumerateRunes().Count() >= 3;

    /// <summary>The clause and <c>$search</c> value that find <paramref name="search"/> in a message or exception.</summary>
    private static (string Clause, string Value) SearchFilter(string search)
    {
        if (IsIndexed(search))
        {
            // Served by the trigram index: a quoted phrase is a case-insensitive substring match in either
            // column. Inside the quotes every character is literal, a doubled quote included — so `%` and `_`
            // mean themselves, as they did escaped in LIKE.
            return (
                "Sequence IN (SELECT rowid FROM RaskLogSearch WHERE RaskLogSearch MATCH $search)",
                "\"" + search.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"");
        }

        // A trigram index has nothing to look up for fewer than three characters, so a one- or two-character
        // search is the scan it always was.
        return (
            @"(Message LIKE $search ESCAPE '\' OR Exception LIKE $search ESCAPE '\')",
            $"%{EscapeLike(search)}%");
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

    /// <summary>A key as the text it is stored as: the 36 characters of <c>D</c>, in uppercase.</summary>
    internal static string FormatId(Guid id) =>
        string.Create(36, id, static (text, value) =>
        {
            value.TryFormat(text, out _, "D");
            Ascii.ToUpperInPlace(text, out _);
        });

    internal static DateTimeOffset ParseTimestamp(string value) =>
        new(DateTime.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
