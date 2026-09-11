using Microsoft.EntityFrameworkCore;

namespace Rask.Logging;

/// <summary>
/// The <see cref="ILogs"/> implementation over the application's own database: the <c>RaskLog</c> table that
/// <c>modelBuilder.AddRaskLogging()</c> maps onto <typeparamref name="TContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// The server-database counterpart of <see cref="SqliteLogStore"/>, and the reasons that store keeps a file of its
/// own weigh differently here. PostgreSQL and SQL Server lock rows, not the whole database, so a machine-rate appender
/// does not queue the request path behind a single write lock. And every operation runs on a context — and so a
/// connection — of its own, created from the factory: a line written while the application's transaction is failing
/// commits on its own and survives the rollback.
/// </para>
/// <para>
/// Queries are portable LINQ rather than provider SQL. Text search lower-cases both sides, and the scope filter looks
/// for the JSON-encoded <c>"key":"value"</c> text instead of calling a JSON function no two providers spell alike.
/// Every operation runs inside <see cref="LogStoreScope"/>, so the SQL EF Core logs on the store's behalf is not
/// captured back into it.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The application context whose model maps the log table.</typeparam>
internal sealed class DbContextLogStore<TContext>(IDbContextFactory<TContext> contextFactory, TimeProvider timeProvider)
    : ILogs
    where TContext : DbContext
{
    // The file store's page: an unbounded DELETE would hold its locks for the length of a whole sweep.
    private const int PurgePageSize = 1000;

    public async Task AppendAsync(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return;
        }

        using var scope = LogStoreScope.Enter();
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Nothing is modified after Add, so change detection would be a graph walk per row for nothing.
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var entries = db.Set<LogEntry>();
            foreach (var record in records)
            {
                entries.Add(LogEntry.From(record));
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 1000);

        using var scope = LogStoreScope.Enter();
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var matching = Filter(db.Set<LogEntry>().AsNoTracking(), query);

            var total = await matching.LongCountAsync(cancellationToken).ConfigureAwait(false);
            var offset = (long)(page - 1) * pageSize;
            if (total == 0 || offset >= total)
            {
                return total == 0 ? LogPage.Empty(page, pageSize) : new LogPage([], total, page, pageSize);
            }

            // Ordered by Id, not Timestamp: the id is monotonic per insert, so entries logged in the same clock tick
            // keep a stable order, and a stable order is the only way a page boundary neither drops nor repeats a row.
            var rows = await matching
                .OrderByDescending(e => e.Id)
                .Skip((int)offset)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return new LogPage(rows.ConvertAll(e => e.ToRecord()), total, page, pageSize);
        }
    }

    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = LogStoreScope.Enter();
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Set<LogEntry>()
                .Select(e => e.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        using var scope = LogStoreScope.Enter();
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Set<LogEntry>().LongCountAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> PurgeAsync(TimeSpan retention, int maxRows, CancellationToken cancellationToken = default)
    {
        using var scope = LogStoreScope.Enter();
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var entries = db.Set<LogEntry>();
            var removed = 0;

            if (retention > TimeSpan.Zero)
            {
                var cutoff = (timeProvider.GetUtcNow() - retention).UtcDateTime;
                removed += await DeletePagesAsync(entries.Where(e => e.Timestamp < cutoff), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (maxRows > 0)
            {
                // Resolved once per sweep, as in the file store: the id at offset maxRows is the newest row past the
                // cap, so everything at or below it goes. Rows arriving mid-sweep are left for the next one.
                var threshold = await entries
                    .OrderByDescending(e => e.Id)
                    .Skip(maxRows)
                    .Select(e => (long?)e.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (threshold is { } newestPastTheCap)
                {
                    removed += await DeletePagesAsync(entries.Where(e => e.Id <= newestPastTheCap), cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return removed;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        using var scope = LogStoreScope.Enter();
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await db.Set<LogEntry>().ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes <paramref name="doomed"/> a page at a time until none is left, reading each page's ids first.
    /// </summary>
    /// <remarks>
    /// Ids first, then a delete by id, rather than a <c>DELETE … LIMIT</c>: no two providers spell a bounded delete
    /// alike. The loop ends on how many ids were <em>read</em>, not deleted, so two hosts sweeping the same table at
    /// once — each deleting some of the other's page — still drain it, and each row is counted by the host that
    /// actually removed it.
    /// </remarks>
    private static async Task<int> DeletePagesAsync(IQueryable<LogEntry> doomed, CancellationToken cancellationToken)
    {
        var removed = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var ids = await doomed
                .OrderBy(e => e.Id)
                .Select(e => e.Id)
                .Take(PurgePageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ids.Count == 0)
            {
                break;
            }

            removed += await doomed
                .Where(e => ids.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ids.Count < PurgePageSize)
            {
                break;
            }
        }

        return removed;
    }

    /// <summary>Applies <paramref name="query"/>'s filters. Every value the caller supplied travels as a parameter.</summary>
    private static IQueryable<LogEntry> Filter(IQueryable<LogEntry> entries, LogQuery query)
    {
        if (query.MinimumLevel is { } minimumLevel)
        {
            entries = entries.Where(e => e.Level >= minimumLevel);
        }

        // Lower-cased on both sides: providers disagree on whether a plain comparison ignores case (SQL Server's
        // default collation does, PostgreSQL's does not), and LogQuery promises it does. EF Core escapes the
        // pattern, so a % or _ in the text is literal.
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.ToLowerInvariant();
            entries = entries.Where(e => e.Category.ToLower().Contains(category));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLowerInvariant();
            entries = entries.Where(e =>
                e.Message.ToLower().Contains(search)
                || (e.Exception != null && e.Exception.ToLower().Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(query.ScopeKey))
        {
            var fragment = LogScopeJson.Fragment(
                query.ScopeKey,
                string.IsNullOrWhiteSpace(query.ScopeValue) ? null : query.ScopeValue);
            entries = entries.Where(e => e.Scopes != null && e.Scopes.Contains(fragment));
        }

        if (query.From is { } from)
        {
            var fromUtc = from.UtcDateTime;
            entries = entries.Where(e => e.Timestamp >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.UtcDateTime;
            entries = entries.Where(e => e.Timestamp <= toUtc);
        }

        return entries;
    }
}
