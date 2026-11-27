using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Rask.Data;
using Rask.Values;

namespace Rask.SqlServer.Tests;

/// <summary>A refuelling's moment, as an app declares one.</summary>
public sealed partial record RefuelledAt(DateTime Value) : Moment(Value);

public sealed class Refuelling
{
    public int Id { get; set; }

    public RefuelledAt At { get; set; } = null!;
}

/// <summary>A Rask context holding a moment: stored as UTC on SQL Server too.</summary>
public sealed class RefuellingContext(DbContextOptions<RefuellingContext> options) : RaskDbContext(options)
{
    public DbSet<Refuelling> Refuellings => Set<Refuelling>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ValueObjectColumn.Map<RefuelledAt, DateTime>(modelBuilder.Entity<Refuelling>().Property(r => r.At), v => v.Value);
    }
}

/// <summary>The same table before Rask stored moments as UTC: the wall clock in a datetime2.</summary>
public sealed class WallRefuelling
{
    public int Id { get; set; }

    public DateTime At { get; set; }
}

public sealed class WallRefuellingContext(DbContextOptions<WallRefuellingContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<WallRefuelling>().ToTable("Refuellings");
}

/// <summary>
///     On SQL Server a moment is kept as UTC in its <c>datetime2</c>: its day is read in the app's zone with
///     <c>AT TIME ZONE</c>, and a column that kept the wall clock is rewritten by its migration as it applies.
/// </summary>
public sealed class MomentUtcTests
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static readonly IServiceProvider Services = new ConfigurationServices(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Rask:ConnectionStrings:App"] = "Server=unused;Database=unused" }).Build());

    [Fact]
    public void A_moment_s_day_is_read_on_the_app_s_wall_calendar()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        using var context = new RefuellingContext(On<RefuellingContext>());

        var sql = context.Refuellings.Where(r => r.At.Day == new DateOnly(2026, 10, 25)).ToQueryString();

        Assert.Contains(
            "CAST(([r].[At] AT TIME ZONE N'UTC') AT TIME ZONE N'Central Europe Standard Time' AS date)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_query_run_again_under_another_zone_reads_that_zone_s_day()
    {
        using var context = new RefuellingContext(On<RefuellingContext>());
        var day = new DateOnly(2026, 10, 25);

        var inBudapest = InZone(Budapest, () => context.Refuellings.Where(r => r.At.Day == day).ToQueryString());
        var inNewYork = InZone(NewYork, () => context.Refuellings.Where(r => r.At.Day == day).ToQueryString());

        Assert.Contains("AT TIME ZONE N'Central Europe Standard Time'", inBudapest, StringComparison.Ordinal);
        Assert.Contains("AT TIME ZONE N'Eastern Standard Time'", inNewYork, StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_that_kept_the_wall_clock_is_rewritten_to_utc_by_its_migration_in_the_app_s_zone()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        using var wall = new WallRefuellingContext(On<WallRefuellingContext>());
        using var today = new RefuellingContext(On<RefuellingContext>());
        var before = wall.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var after = today.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var operations = today.GetService<IMigrationsModelDiffer>().GetDifferences(before, after);
        var commands = today.GetService<IMigrationsSqlGenerator>().Generate(operations, today.Model);

        Assert.Single(operations.OfType<SqlOperation>());
        Assert.Contains(
            commands,
            c => c.CommandText.Contains(
                "UPDATE [Refuellings] SET [At] = CAST(([At] AT TIME ZONE N'Central Europe Standard Time') AT TIME ZONE 'UTC' AS datetime2) WHERE [At] IS NOT NULL;",
                StringComparison.Ordinal));
    }

    [Fact]
    public void In_an_app_whose_zone_is_utc_nothing_is_rewritten()
    {
        using var zone = AmbientClock.UseZone(TimeZoneInfo.Utc);
        using var wall = new WallRefuellingContext(On<WallRefuellingContext>());
        using var today = new RefuellingContext(On<RefuellingContext>());
        var before = wall.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var after = today.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var operations = today.GetService<IMigrationsModelDiffer>().GetDifferences(before, after);
        var commands = today.GetService<IMigrationsSqlGenerator>().Generate(operations, today.Model);

        Assert.DoesNotContain(commands, c => c.CommandText.Contains("UPDATE", StringComparison.Ordinal));
    }

    private static string InZone(TimeZoneInfo zone, Func<string> query)
    {
        using var ambient = AmbientClock.UseZone(zone);
        return query();
    }

    private static DbContextOptions<TContext> On<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseRaskSqlServer(Services).Options;

    // Just the configuration: that is all UseRaskSqlServer asks the provider for.
    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
