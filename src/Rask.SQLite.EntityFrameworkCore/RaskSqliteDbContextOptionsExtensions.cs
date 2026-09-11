using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Rask.Hosting.Shared;

namespace Rask.SQLite;

/// <summary>
/// Entity Framework Core entry point: a drop-in replacement for <c>UseSqlite</c> that also wires the
/// production pragmas onto every connection the context opens.
/// </summary>
public static class RaskSqliteDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use SQLite with the <c>Rask:ConnectionStrings:App</c> connection string and
    /// applies the <see cref="SqliteOptions"/> — production defaults, then the <c>Rask:Sqlite</c> configuration
    /// section, then <paramref name="configure"/> — on every connection open.
    /// <code>
    /// builder.Services.AddDbContextFactory&lt;AppDbContext&gt;((sp, o) =&gt; o.UseRaskSqlite(sp));
    /// </code>
    /// </summary>
    /// <param name="optionsBuilder">The context options builder being configured.</param>
    /// <param name="services">The application's services, which carry its configuration.</param>
    /// <param name="configure">Overrides for the pragma settings, applied after the <c>Rask:Sqlite</c> section.</param>
    /// <remarks>
    /// <para>
    /// A missing <c>Rask:ConnectionStrings:App</c> is an error that names the key rather than a fallback file: a
    /// database opened wherever the process happens to be running is how a container loses its data on the next
    /// deploy. A design-time factory (<c>dotnet ef</c>) builds a small service provider carrying its configuration
    /// and passes that.
    /// </para>
    /// <para>
    /// Set <c>Retry.Enabled</c> to register the fair-interval <see cref="RaskSqliteExecutionStrategy"/>
    /// so <c>SaveChanges</c> and queries retry on <c>SQLITE_BUSY</c>/<c>SQLITE_LOCKED</c>. That also turns
    /// SQLite's native busy handler off (<c>busy_timeout=0</c>) and lowers Microsoft.Data.Sqlite's own
    /// blocking command timeout, so the async strategy owns the waiting rather than a thread parked inside
    /// native code. The implicit <c>SaveChanges</c> transaction remains <c>DEFERRED</c> (a write-only batch
    /// already takes the write lock on its first statement); wrap a read-then-write transaction in
    /// <see cref="SqliteConnectionExtensions.BeginImmediate"/> to avoid the deferred-upgrade deadlock.
    /// </para>
    /// <para>
    /// Set <c>StrictTables</c> to create tables as SQLite <c>STRICT</c> tables, so the store enforces
    /// each column's declared type rather than coercing whatever it is handed. Strictness is a property of
    /// the table, so it affects newly created tables only, and a model with an explicit
    /// <c>HasColumnType(...)</c> outside SQLite's six allowed type names is rejected at creation time. See
    /// <see cref="RaskSqliteStrictMigrationsSqlGenerator"/>.
    /// </para>
    /// <para>
    /// The options are read each time EF builds the context options, which <c>AddDbContextFactory</c> does once;
    /// <c>AddDbContext</c> does it per scope by default.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseRaskSqlite(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider services,
        Action<SqliteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(services);

        var connectionString = RaskOptionsRegistration.ConnectionString(services, "App");
        var options = RaskOptionsRegistration.BindNow<SqliteOptions>(
            services, "Rask:Sqlite", static (section, o) => section.Bind(o), configure);

        var retry = options.Retry;
        try
        {
            retry.Validate();

            if (retry.Enabled)
            {
                // The async execution strategy owns waiting: turn off SQLite's native busy handler so BUSY
                // surfaces to the strategy instead of blocking a thread inside native code.
                options.BusyTimeout = TimeSpan.Zero;
            }

            options.Validate();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Reported like every other bad setting — which section, and why — since the value may well have
            // come from appsettings rather than the callback.
            throw new OptionsValidationException(
                Microsoft.Extensions.Options.Options.DefaultName, typeof(SqliteOptions), [$"Rask:Sqlite: {ex.Message}"]);
        }

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
    /// <see cref="UseRaskSqlite(DbContextOptionsBuilder, IServiceProvider, Action{SqliteOptions}?)"/>, so
    /// <c>new DbContextOptionsBuilder&lt;TContext&gt;().UseRaskSqlite(services).Options</c> keeps its
    /// <see cref="DbContextOptions{TContext}"/> type.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseRaskSqlite<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        IServiceProvider services,
        Action<SqliteOptions>? configure = null)
        where TContext : DbContext
    {
        UseRaskSqlite((DbContextOptionsBuilder)optionsBuilder, services, configure);
        return optionsBuilder;
    }
}
