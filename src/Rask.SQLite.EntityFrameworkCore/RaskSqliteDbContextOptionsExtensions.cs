using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite;

/// <summary>
/// Entity Framework Core entry point: a drop-in replacement for <c>UseSqlite</c> that also wires the
/// Rails-style production pragmas onto every connection the context opens.
/// </summary>
public static class RaskSqliteDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use SQLite with <paramref name="connectionString"/> and applies the
    /// <see cref="SqlitePragmaOptions"/> (Rails production defaults, overridable via
    /// <paramref name="configure"/>) on every connection open. Swap your <c>UseSqlite(cs)</c> for
    /// <c>UseRaskSqlite(cs)</c> and you are done.
    /// </summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <param name="configure">Overrides for the production pragma defaults.</param>
    /// <param name="configureRetry">
    /// When supplied (even as an empty <c>_ =&gt; { }</c>), registers the Rails-style fair-interval
    /// <see cref="RaskSqliteExecutionStrategy"/> so <c>SaveChanges</c> and queries retry on
    /// <c>SQLITE_BUSY</c>/<c>SQLITE_LOCKED</c>. Enabling it turns SQLite's native busy handler off
    /// (<c>busy_timeout=0</c>) and lowers Microsoft.Data.Sqlite's own blocking command-timeout so the
    /// async strategy owns the waiting. The implicit <c>SaveChanges</c> transaction remains
    /// <c>DEFERRED</c> (a write-only batch already takes the write lock on its first statement); wrap a
    /// read-then-write transaction in <see cref="SqliteConnectionExtensions.BeginImmediate"/> to avoid the
    /// deferred-upgrade deadlock.
    /// </param>
    public static DbContextOptionsBuilder UseRaskSqlite(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<SqlitePragmaOptions>? configure = null,
        Action<SqliteBusyRetryOptions>? configureRetry = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var options = new SqlitePragmaOptions();
        configure?.Invoke(options);

        var retry = new SqliteBusyRetryOptions();
        var retryEnabled = configureRetry is not null;
        configureRetry?.Invoke(retry);
        retry.Validate();

        if (retryEnabled)
        {
            // The async execution strategy owns waiting: turn off SQLite's native busy handler so BUSY
            // surfaces to the strategy instead of blocking a thread inside native code.
            options.BusyTimeout = TimeSpan.Zero;
        }

        options.Validate();

        return optionsBuilder
            .UseSqlite(connectionString, sqlite =>
            {
                if (retryEnabled)
                {
                    // Bound Microsoft.Data.Sqlite's own synchronous Thread.Sleep busy-retry to its ~1s
                    // minimum, so a contended command hands control back to the fair-interval strategy
                    // quickly instead of blocking a thread for the default 30s. CommandTimeout only limits
                    // waiting for a lock, not query runtime.
                    sqlite.CommandTimeout(1);
                    sqlite.ExecutionStrategy(dependencies => new RaskSqliteExecutionStrategy(dependencies, retry));
                }
            })
            .AddInterceptors(new RaskSqliteConnectionInterceptor(options));
    }

    /// <summary>
    /// The strongly-typed overload of
    /// <see cref="UseRaskSqlite(DbContextOptionsBuilder, string, Action{SqlitePragmaOptions}?, Action{SqliteBusyRetryOptions}?)"/>, so
    /// <c>new DbContextOptionsBuilder&lt;TContext&gt;().UseRaskSqlite(cs).Options</c> keeps its
    /// <see cref="DbContextOptions{TContext}"/> type.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseRaskSqlite<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        Action<SqlitePragmaOptions>? configure = null,
        Action<SqliteBusyRetryOptions>? configureRetry = null)
        where TContext : DbContext
    {
        UseRaskSqlite((DbContextOptionsBuilder)optionsBuilder, connectionString, configure, configureRetry);
        return optionsBuilder;
    }
}
