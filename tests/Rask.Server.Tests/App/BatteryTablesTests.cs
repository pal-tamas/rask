using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Background;
using Rask.Caching;
using Rask.Data;

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

/// <summary>Every battery on, with every string capped at one character unless its battery says otherwise.</summary>
public sealed class CappedStringsContext(DbContextOptions<RaskAppDbContext> options) : RaskAppDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<string>().HaveMaxLength(1);
    }
}

/// <summary>Rask's context under TA's rules, on SQL Server, with one battery table renamed past the identifier limit.</summary>
public sealed class RuledAppContext(DbContextOptions<RaskAppDbContext> options) : RaskAppDbContext(options)
{
    internal static readonly string LongTable = new('c', 125);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<string>().HaveMaxLength(1000);
        configurationBuilder.ConstraintNames(n => n.Kind switch
        {
            ConstraintKind.PrimaryKey => $"{n.Table}_pkey",
            ConstraintKind.ForeignKey => $"{n.Table}_{n.Columns[0]}_fkey",
            ConstraintKind.Unique => $"{n.Table}_{n.Joined}_key",
            ConstraintKind.Index => $"{n.Table}_{n.Joined}_idx",
            ConstraintKind.Default => $"{n.Table}_{n.Columns[0]}_df",
            _ => null,
        });
        configurationBuilder.DeleteBehavior(children: DeleteBehavior.ClientCascade, others: DeleteBehavior.NoAction);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<CacheEntry>().ToTable(LongTable);
        modelBuilder.Entity<Job>().Property(j => j.Attempts).HasDefaultValue(0);
        modelBuilder.Entity<Job>().Property(j => j.Lane).HasDefaultValue("main")
            .Metadata.SetDefaultConstraintName("jobs_lane_as_written");
    }
}

/// <summary>
/// A battery's tables are mapped while it is on: a new app's migrations carry what it uses, and turning one off in an
/// app that has its tables never drops them.
/// </summary>
public sealed class BatteryTablesTests
{
    private const BatteryTables JobsOff = BatteryTables.All & ~BatteryTables.Jobs;

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
    public void Every_battery_column_states_its_own_length_so_an_app_wide_one_never_truncates_it()
    {
        using var context = Open(o => new CappedStringsContext(o), BatteryTables.All, sqlServer: true);

        var model = context.GetService<IDesignTimeModel>().Model;

        var capped = model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Where(p => p.ClrType == typeof(string)).Select(p => (Entity: e, Property: p)))
            .Where(x => (x.Property.PropertyInfo?.DeclaringType ?? x.Entity.ClrType).Assembly != typeof(BatteryTablesTests).Assembly)
            .Where(x => x.Property.GetMaxLength() == 1)
            .Select(x => $"{x.Entity.ClrType.Name}.{x.Property.Name}")
            .ToList();
        Assert.True(capped.Count == 0, "Capped by the app-wide length: " + string.Join(", ", capped));
        Assert.Equal("nvarchar(max)", model.FindEntityType(typeof(Job))!.FindProperty(nameof(Job.Payload))!.GetColumnType());
    }

    [Fact]
    public void An_app_s_constraint_names_reach_every_battery_table_and_are_cut_to_SQL_Server_s_128()
    {
        using var context = Open(o => new RuledAppContext(o), BatteryTables.All, sqlServer: true);

        var model = context.GetService<IDesignTimeModel>().Model;

        var jobs = model.FindEntityType(typeof(Job))!;
        var table = jobs.GetTableName();
        Assert.Equal($"{table}_pkey", jobs.FindPrimaryKey()!.GetName());
        Assert.Equal($"{table}_WaitingKey_key", jobs.GetIndexes().Single(i => i.IsUnique).GetDatabaseName());
        Assert.Equal($"{table}_Attempts_df", jobs.FindProperty(nameof(Job.Attempts))!.GetDefaultConstraintName());
        Assert.Equal("jobs_lane_as_written", jobs.FindProperty(nameof(Job.Lane))!.GetDefaultConstraintName());
        var cut = model.FindEntityType(typeof(CacheEntry))!.FindPrimaryKey()!.GetName();
        Assert.Equal(($"{RuledAppContext.LongTable}_pkey")[..128], cut);
    }

    private static T Open<T>(Func<DbContextOptions<RaskAppDbContext>, T> create, BatteryTables tables, bool sqlServer = false)
        where T : DbContext
    {
        var migrations = typeof(BatteryTablesTests).Assembly.GetName().Name;
        var builder = new DbContextOptionsBuilder<RaskAppDbContext>();
        if (sqlServer)
        {
            builder.UseSqlServer("Server=unused;Database=unused", o => o.MigrationsAssembly(migrations));
        }
        else
        {
            builder.UseSqlite("Data Source=:memory:", o => o.MigrationsAssembly(migrations));
        }

        return create((DbContextOptions<RaskAppDbContext>)builder.WithBatteryTables(tables).Options);
    }

    private static string JobsTable(DbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Job))!.GetTableName()!;

    // What `dotnet ef migrations add` runs: the model the last migration left, against the one the app builds now.
    private static IReadOnlyList<MigrationOperation> Diff(DbContext before, DbContext after) =>
        after.GetService<IMigrationsModelDiffer>().GetDifferences(
            before.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            after.GetService<IDesignTimeModel>().Model.GetRelationalModel());
}
