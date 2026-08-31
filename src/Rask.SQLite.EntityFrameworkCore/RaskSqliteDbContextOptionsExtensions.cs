using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Rask.SQLite;

/// <summary>
/// Entity Framework Core entry point: a drop-in replacement for <c>UseSqlite</c> that also wires the
/// production pragmas onto every connection the context opens.
/// </summary>
public static class RaskSqliteDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use SQLite with <paramref name="connectionString"/> and applies the
    /// <see cref="SqliteOptions"/> (production defaults, overridable via
    /// <paramref name="configure"/>) on every connection open. Swap your <c>UseSqlite(cs)</c> for
    /// <c>UseRaskSqlite(cs)</c> and you are done.
    /// </summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <param name="configure">Overrides for the production pragma defaults.</param>
    /// <remarks>
    /// Set <c>o.Retry.Enabled</c> to register the fair-interval <see cref="RaskSqliteExecutionStrategy"/>
    /// so <c>SaveChanges</c> and queries retry on <c>SQLITE_BUSY</c>/<c>SQLITE_LOCKED</c>. That also turns
    /// SQLite's native busy handler off (<c>busy_timeout=0</c>) and lowers Microsoft.Data.Sqlite's own
    /// blocking command timeout, so the async strategy owns the waiting rather than a thread parked inside
    /// native code. The implicit <c>SaveChanges</c> transaction remains <c>DEFERRED</c> (a write-only batch
    /// already takes the write lock on its first statement); wrap a read-then-write transaction in
    /// <see cref="SqliteConnectionExtensions.BeginImmediate"/> to avoid the deferred-upgrade deadlock.
    /// <para>
    /// Set <c>o.StrictTables</c> to create tables as SQLite <c>STRICT</c> tables, so the store enforces
    /// each column's declared type rather than coercing whatever it is handed. Strictness is a property of
    /// the table, so it affects newly created tables only, and a model with an explicit
    /// <c>HasColumnType(...)</c> outside SQLite's six allowed type names is rejected at creation time. See
    /// <see cref="RaskSqliteStrictMigrationsSqlGenerator"/>.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseRaskSqlite(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<SqliteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var options = new SqliteOptions();
        configure?.Invoke(options);

        var retry = options.Retry;
        retry.Validate();

        if (retry.Enabled)
        {
            // The async execution strategy owns waiting: turn off SQLite's native busy handler so BUSY
            // surfaces to the strategy instead of blocking a thread inside native code.
            options.BusyTimeout = TimeSpan.Zero;
        }

        options.Validate();

        // EF Core resolves exactly one IMigrationsSqlGenerator, so this is a single choice rather than two
        // replacements: registering a strict generator and a range-exclusion generator separately would keep
        // only whichever was replaced last, silently dropping the other feature. Which one is wanted depends
        // on the flag, so the combination has a type of its own. Range-exclusion DDL is inert unless an
        // entity declares HasNonOverlappingRange — the generator finds no spec to emit.
        if (options.StrictTables)
        {
            optionsBuilder.ReplaceService<IMigrationsSqlGenerator, RaskSqliteStrictRangeExclusionSqlGenerator>();
        }
        else
        {
            optionsBuilder.ReplaceService<IMigrationsSqlGenerator, RaskSqliteRangeExclusionSqlGenerator>();
        }

        return optionsBuilder
            // Inert too: it only reacts to the error the range-exclusion triggers raise.
            .AddInterceptors(new RaskSqliteRangeExclusionInterceptor())
            .UseSqlite(connectionString, sqlite =>
            {
                if (retry.Enabled)
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
    /// <see cref="UseRaskSqlite(DbContextOptionsBuilder, string, Action{SqliteOptions}?)"/>, so
    /// <c>new DbContextOptionsBuilder&lt;TContext&gt;().UseRaskSqlite(cs).Options</c> keeps its
    /// <see cref="DbContextOptions{TContext}"/> type.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseRaskSqlite<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        Action<SqliteOptions>? configure = null)
        where TContext : DbContext
    {
        UseRaskSqlite((DbContextOptionsBuilder)optionsBuilder, connectionString, configure);
        return optionsBuilder;
    }
}
