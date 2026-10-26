using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Rask.Auth;
using Rask.Background;
using Rask.Caching;
using Rask.Data;
using Rask.Logging;
using Rask.Mailing;
using Rask.Outbox;
using Rask.Storage;
using Rask.WebPush;

namespace Rask;

/// <summary>
/// The database a Rask app gets when it does not write a <see cref="DbContext" /> of its own: your
/// entities, plus every battery's tables.
/// </summary>
/// <remarks>
/// <para>
/// The entities come from <see cref="ModelRegistry" /> — everything deriving from <see cref="Aggregate{TId}" />
/// that the source generator found, with Rask's conventions applied and each entity's own static
/// <c>Configure</c> run last. Declaring the entity is the whole of what an app
/// does: there is no context to write, no <c>DbSet</c> property to remember, and no
/// <see cref="IEntityTypeConfiguration{TEntity}" /> class.
/// </para>
/// <para>
/// <b>A battery's tables are mapped while it is on</b>, the way a Laravel app has no jobs table until it asks for one:
/// switching a battery on is a migration (<c>rask db add</c>), and a new app's first migration carries only what it
/// uses. Switching one OFF never drops anything — an app whose migrations already created the tables keeps them,
/// excluded from migrations, so the next migration does not touch them. The model is cached per set of switches, so two
/// apps in one process (a test suite) never share one built for the other.
/// The log's table also follows the database: mapped on PostgreSQL or SQL Server, where RaskApp keeps the log in this
/// database, and left out on SQLite, where the log keeps a file of its own.
/// </para>
/// <para>
/// An app that outgrows this writes its own context; registering an
/// <c>IDbContextFactory&lt;YourContext&gt;</c> is enough for Rask to bind everything to that one instead,
/// and this type is then never constructed. Copy the <c>OnModelCreating</c> below as the starting point.
/// </para>
/// </remarks>
public class RaskAppDbContext : RaskDbContext
{
    private readonly BatteryTables _tables;
    private readonly string? _migrationsAssembly;

    /// <summary>Creates the context with the options the host registered.</summary>
    public RaskAppDbContext(DbContextOptions<RaskAppDbContext> options)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _tables = BatteryTablesExtension.Of(options);
        _migrationsAssembly = options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault() is { } relational
            ? relational.MigrationsAssembly ?? relational.MigrationsAssemblyObject?.GetName().Name
            : null;
    }

    /// <summary>What this context's model is built from besides its type — what <see cref="RaskAppModelCacheKeyFactory" /> keys on.</summary>
    internal (BatteryTables Tables, string? MigrationsAssembly) ModelShape => (_tables, _migrationsAssembly);

    /// <inheritdoc />
    /// <remarks>
    /// Every instance carries the battery switches — every battery on, when it was built without them — because they
    /// are what registers the model cache key that tells one set of switches from another. Through the options rather
    /// than <c>ReplaceService</c>, so a context built with them shares EF Core's internal services with every other.
    /// </remarks>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        base.OnConfiguring(optionsBuilder);
        if (optionsBuilder.Options.FindExtension<BatteryTablesExtension>() is null)
        {
            optionsBuilder.WithBatteryTables(_tables);
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // The app's own entities first, so a battery table can never be shadowed by one of them.
        base.OnModelCreating(modelBuilder);

        // Read once, and only when a battery is off: the model the app's last migration left, which the next one is
        // diffed against.
        var snapshot = new Lazy<IReadOnlyModel?>(() => this.GetService<IMigrationsAssembly>().ModelSnapshot?.Model);

        Map(modelBuilder, BatteryTables.Auth, static m => m.AddRaskAuth(), snapshot);
        Map(modelBuilder, BatteryTables.Cache, static m => m.AddRaskCache(), snapshot);
        Map(modelBuilder, BatteryTables.Jobs, static m => m.AddRaskJobs(), snapshot);
        Map(modelBuilder, BatteryTables.Mail, static m => m.AddRaskMail(), snapshot);
        Map(modelBuilder, BatteryTables.Storage, static m => m.AddRaskStorage(), snapshot);
        Map(modelBuilder, BatteryTables.Push, static m => m.AddRaskWebPush(), snapshot);

        // The outbox follows the database: a handler chooses durability, so there is no switch to turn it off.
        modelBuilder.AddRaskOutbox();

        // The log table only where the log lives in this database. On SQLite the log keeps a file of its own
        // (logs.db), so a SQLite app's migrations gain no table it never writes; on PostgreSQL or SQL Server RaskApp's
        // log store writes here.
        if (!Database.IsSqlite())
        {
            Map(modelBuilder, BatteryTables.Logs, static m => m.AddRaskLogging(), snapshot);
        }
    }

    private void Map(
        ModelBuilder modelBuilder, BatteryTables battery, Func<ModelBuilder, ModelBuilder> map, Lazy<IReadOnlyModel?> snapshot) =>
        BatteryTableMap.Map(modelBuilder, _tables.HasFlag(battery), map, () => snapshot.Value);
}
