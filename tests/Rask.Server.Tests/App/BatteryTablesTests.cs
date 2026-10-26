using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;
using Rask.Caching;

namespace Rask.Server.Tests.App;

/// <summary>An app that already ran migrations with every battery on, and has since turned jobs off.</summary>
public sealed class KeptJobsContext(DbContextOptions<RaskAppDbContext> options) : RaskAppDbContext(options);

/// <summary>What its last migration left: the jobs table among the rest. Only the name is read.</summary>
[DbContext(typeof(KeptJobsContext))]
public sealed class KeptJobsContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity("Rask.Background.Job", b =>
        {
            b.Property<long>("Id");
            b.HasKey("Id");
            b.ToTable("Jobs");
        });
}

/// <summary>An app with migrations that never had jobs: its snapshot holds other tables only.</summary>
public sealed class NeverHadJobsContext(DbContextOptions<RaskAppDbContext> options) : RaskAppDbContext(options);

[DbContext(typeof(NeverHadJobsContext))]
public sealed class NeverHadJobsContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity("Rask.Caching.CacheEntry", b =>
        {
            b.Property<string>("Key");
            b.HasKey("Key");
            b.ToTable("CacheEntries");
        });
}

/// <summary>
/// A battery's tables are mapped while it is on: a new app's migrations carry what it uses, and turning one off in an
/// app that has its tables never drops them.
/// </summary>
/// <remarks>The SQL Server half — column types, the identifier limit — is <c>Rask.SqlServer.Tests</c>' RaskAppModelTests.</remarks>
public sealed class BatteryTablesTests
{
    private const BatteryTables JobsOff = BatteryTables.All & ~BatteryTables.Jobs;

    // The configuration RaskApp gives a SQLite app, so these contexts are opened exactly as its are — and share EF Core's
    // internal services with the apps the rest of the suite builds: EF refuses a process that builds more than twenty.
    private static readonly IServiceProvider SqliteApp = new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(RaskBatteryWiring.DefaultsFor(RaskDatabaseProvider.Sqlite))
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", "Data Source=:memory:")])
            .Build())
        .BuildServiceProvider();

    [Fact]
    public void A_battery_that_is_off_has_no_table_and_every_switch_gets_a_model_of_its_own()
    {
        using var on = Open(o => new RaskAppDbContext(o), BatteryTables.All);
        using var off = Open(o => new RaskAppDbContext(o), JobsOff);

        var withJobs = on.GetService<IDesignTimeModel>().Model;
        var withoutJobs = off.GetService<IDesignTimeModel>().Model;

        Assert.NotNull(withJobs.FindEntityType(typeof(Job)));
        Assert.Null(withoutJobs.FindEntityType(typeof(Job)));
        Assert.NotNull(withoutJobs.FindEntityType(typeof(CacheEntry)));
        Assert.NotSame(withJobs, withoutJobs);
    }

    [Fact]
    public void Leaving_a_table_out_of_the_model_is_what_a_migration_would_drop()
    {
        using var on = Open(o => new RaskAppDbContext(o), BatteryTables.All);
        using var off = Open(o => new RaskAppDbContext(o), JobsOff);

        var operations = Diff(on, off);

        Assert.Contains(operations, op => op is DropTableOperation { Name: var name } && name == JobsTable(on));
    }

    [Fact]
    public void Turning_a_battery_off_in_an_app_whose_migrations_made_its_table_never_drops_it()
    {
        using var on = Open(o => new KeptJobsContext(o), BatteryTables.All);
        using var off = Open(o => new KeptJobsContext(o), JobsOff);
        var table = JobsTable(on);

        var operations = Diff(on, off);

        var kept = off.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Job))!;
        Assert.True(kept.IsTableExcludedFromMigrations());
        Assert.DoesNotContain(operations, op => op is DropTableOperation);
        Assert.DoesNotContain(operations, op => op is ITableMigrationOperation { Table: var t } && t == table);
    }

    [Fact]
    public void An_app_whose_migrations_never_made_a_battery_s_table_does_not_get_one_while_it_is_off()
    {
        using var off = Open(o => new NeverHadJobsContext(o), JobsOff);

        var model = off.GetService<IDesignTimeModel>().Model;

        Assert.Null(model.FindEntityType(typeof(Job)));
        Assert.False(model.FindEntityType(typeof(CacheEntry))!.IsTableExcludedFromMigrations());
    }

    // The migrations are looked for in the context's own assembly, which for KeptJobsContext is this one.
    private static T Open<T>(Func<DbContextOptions<RaskAppDbContext>, T> create, BatteryTables tables)
        where T : DbContext =>
        create((DbContextOptions<RaskAppDbContext>)new DbContextOptionsBuilder<RaskAppDbContext>()
            .UseApplicationServiceProvider(SqliteApp)
            .UseRaskDatabase(SqliteApp)
            .WithBatteryTables(tables)
            .Options);

    private static string JobsTable(DbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Job))!.GetTableName()!;

    // What `dotnet ef migrations add` runs: the model the last migration left, against the one the app builds now.
    private static IReadOnlyList<MigrationOperation> Diff(DbContext before, DbContext after) =>
        after.GetService<IMigrationsModelDiffer>().GetDifferences(
            before.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            after.GetService<IDesignTimeModel>().Model.GetRelationalModel());
}
