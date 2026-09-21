using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Rask.Data;
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

        // Full-text search, inert unless an entity declares HasFullTextSearch. The generator above already emits its
        // DDL; this adds the model and query halves.
        AddFullTextSearchModelAndQuery(optionsBuilder);

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

    /// <summary>
    /// Full-text search on SQLite — <c>HasFullTextSearch</c>, <c>Search(text)</c>, <c>FullText.Highlight</c> and
    /// <c>Snippet</c> — for a context configured with a plain <c>UseSqlite</c>, as a browser app's is.
    /// </summary>
    /// <param name="optionsBuilder">The context's options, already given its SQLite connection.</param>
    /// <returns>The same builder, to chain.</returns>
    /// <remarks>
    /// <para>
    /// <c>UseRaskSqlite</c> turns this on by itself, together with the connection string, pragmas and retry a
    /// server wants. A browser app opens its database with <c>UseSqlite(BrowserSqlite.ConnectionString("app"))</c>
    /// instead and wants none of those, so this registers only what search needs: the annotation that makes adding
    /// or removing it a migration, the migration SQL that builds the FTS5 index and its triggers, and the query
    /// rewrite behind <c>Search</c>.
    /// </para>
    /// <para>
    /// The index is built by a MIGRATION. <c>EnsureCreated</c> creates no index, and a query against it then fails —
    /// apply migrations (<c>Database.MigrateAsync()</c>) instead.
    /// </para>
    /// <para>
    /// EF Core keeps exactly one migrations SQL generator, and the last registration wins. So this installs Rask's
    /// only when the one already registered cannot build the index: calling it after <c>UseRaskSqlite</c> keeps that
    /// call's choice, strict tables included, and calling it twice is harmless.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseRaskFullTextSearch(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var replaced = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.ReplacedServices;
        var generator = replaced?.FirstOrDefault(r => r.Key.Item1 == typeof(IMigrationsSqlGenerator)).Value;
        if (generator is null || !typeof(IFullTextSearchEnforcer).IsAssignableFrom(generator))
        {
            optionsBuilder.ReplaceService<IMigrationsSqlGenerator, RaskSqliteRangeExclusionSqlGenerator>();
        }

        AddFullTextSearchModelAndQuery(optionsBuilder);
        return optionsBuilder;
    }

    /// <inheritdoc cref="UseRaskFullTextSearch(DbContextOptionsBuilder)" />
    public static DbContextOptionsBuilder<TContext> UseRaskFullTextSearch<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseRaskFullTextSearch((DbContextOptionsBuilder)optionsBuilder);

    // The annotation provider reports the declaration on its table so the migrations differ sees it change; EF
    // resolves exactly one, and nothing else in Rask replaces it. The extension carries the query side, and EF keeps
    // one per type, so a second call does not register the rewrite twice.
    private static void AddFullTextSearchModelAndQuery(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ReplaceService<IRelationalAnnotationProvider, RaskSqliteAnnotationProvider>();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new FullTextSearchOptionsExtension());
    }
}
