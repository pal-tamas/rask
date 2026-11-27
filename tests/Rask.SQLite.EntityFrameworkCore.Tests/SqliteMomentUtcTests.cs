using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Data;
using Rask.Values;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

/// <summary>A moment on the clock, as an app declares one.</summary>
public sealed record DutyStart(DateTime Value) : Moment(Value);

public sealed class Duty
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public DutyStart Start { get; set; } = null!;
}

/// <summary>A shift's start as a Rask model maps a moment: Rask's own converted column.</summary>
public sealed class DutyContext(DbContextOptions<DutyContext> options) : DbContext(options)
{
    public DbSet<Duty> Duties => Set<Duty>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        ValueObjectColumn.Map<DutyStart, DateTime>(modelBuilder.Entity<Duty>().Property(s => s.Start), v => v.Value);
}

/// <summary>The same table before Rask stored moments as UTC: a wall-clock DateTime column.</summary>
public sealed class WallDuty
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public DateTime Start { get; set; }
}

public sealed class WallDutyContext(DbContextOptions<WallDutyContext> options) : DbContext(options)
{
    public DbSet<WallDuty> Duties => Set<WallDuty>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<WallDuty>().ToTable("Duties");
}

// UseRaskSqlite keeps a Moment as the UTC instant it holds: ordered, compared and grouped by day as SQL Server and
// PostgreSQL do, the hour the clock shows twice in autumn kept apart, and an older wall-clock column rewritten by its migration.
public sealed class SqliteMomentUtcTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    private readonly InMemorySqlite _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using var context = Duties();
        await context.Database.EnsureCreatedAsync(Cancellation);
        context.Duties.AddRange(
            Starting("first-0230", Utc(2026, 10, 25, 0, 30)),
            Starting("second-0230", Utc(2026, 10, 25, 1, 30)),
            Starting("late-evening", Utc(2026, 10, 24, 22, 30)),
            Starting("summer-noon", Utc(2026, 7, 8, 10, 0)));
        await context.SaveChangesAsync(Cancellation);
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task A_moment_is_stored_as_the_utc_instant_it_holds()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using var context = Duties();

        var stored = await context.Database.SqlQueryRaw<string>("SELECT \"Start\" AS \"Value\" FROM \"Duties\" WHERE \"Code\" = 'summer-noon'")
            .SingleAsync(Cancellation);
        var read = await context.Duties.Where(s => s.Code == "summer-noon").Select(s => s.Start).SingleAsync(Cancellation);

        Assert.Equal("2026-07-08 10:00:00", stored);
        Assert.Equal(new DateTime(2026, 7, 8, 12, 0, 0), read.Value);
        Assert.Equal(new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero), read.Instant);
    }

    [Fact]
    public async Task Both_occurrences_of_the_repeated_hour_come_back_and_order_as_they_happened()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using var context = Duties();

        var ordered = await context.Duties.OrderBy(s => s.Start).Select(s => s.Code).ToListAsync(Cancellation);
        var first = await context.Duties.SingleAsync(s => s.Code == "first-0230", Cancellation);
        var second = await context.Duties.SingleAsync(s => s.Code == "second-0230", Cancellation);

        Assert.Equal(["summer-noon", "late-evening", "first-0230", "second-0230"], ordered);
        Assert.Equal(first.Start.Value, second.Start.Value);
        Assert.Equal(TimeSpan.FromHours(1), second.Start.Instant - first.Start.Instant);
    }

    [Fact]
    public async Task Min_max_and_a_comparison_read_the_instant_a_wall_time_parameter_names_by_the_rule()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using var context = Duties();
        var since = new DateTime(2026, 10, 25, 2, 45, 0); // shown twice: its first occurrence, 00:45Z

        var earliest = await context.Duties.MinAsync(s => s.Start.Value, Cancellation);
        var latest = await context.Duties.MaxAsync(s => s.Start, Cancellation);
        var after = await context.Duties.Where(s => s.Start.Value > since).Select(s => s.Code).ToListAsync(Cancellation);

        Assert.Equal(new DateTime(2026, 7, 8, 12, 0, 0), earliest);
        Assert.Equal(Utc(2026, 10, 25, 1, 30), latest.Instant.UtcDateTime);
        Assert.Equal(["second-0230"], after);
    }

    [Fact]
    public async Task A_query_groups_by_the_day_on_the_app_s_wall_calendar()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using var context = Duties();

        var days = await context.Duties.GroupBy(s => s.Start.Day).Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(g => g.Day).ToListAsync(Cancellation);

        Assert.Equal([(new DateOnly(2026, 7, 8), 1), (new DateOnly(2026, 10, 25), 3)], days.Select(d => (d.Day, d.Count)));
    }

    [Fact]
    public async Task A_part_of_the_time_read_off_the_utc_column_is_refused_with_the_way_to_say_it()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using var context = Duties();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Duties.Where(s => s.Start.Value.Date == new DateTime(2026, 10, 25)).ToListAsync(Cancellation));

        Assert.Contains("'.Day'", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_that_kept_the_wall_clock_owes_a_migration_that_rewrites_it()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        using var wall = new WallDutyContext(new DbContextOptionsBuilder<WallDutyContext>().UseRaskSqliteAt(_database.ConnectionString).Options);
        using var today = Duties();
        var differ = today.GetService<IMigrationsModelDiffer>();
        var before = wall.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var after = today.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var pending = differ.HasDifferences(before, after);
        var rewrite = Assert.Single(differ.GetDifferences(before, after).OfType<SqlOperation>());
        var none = differ.GetDifferences(null, after).OfType<SqlOperation>();

        Assert.True(pending);
        Assert.StartsWith("-- Rask: ", rewrite.Sql, StringComparison.Ordinal);
        Assert.Contains("[Duties].[Start]", rewrite.Sql, StringComparison.Ordinal);
        Assert.Empty(none);
    }

    [Fact]
    public async Task The_rewrite_turns_each_wall_clock_row_into_its_utc_instant_as_the_migration_applies()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using var wall = new WallDutyContext(new DbContextOptionsBuilder<WallDutyContext>().UseRaskSqliteAt(_database.ConnectionString).Options);
        await using var today = Duties();
        await wall.Database.ExecuteSqlRawAsync("DELETE FROM \"Duties\"", Cancellation);
        wall.Duties.AddRange(
            new WallDuty { Code = "winter", Start = new DateTime(2026, 1, 14, 9, 0, 0) },
            new WallDuty { Code = "repeated", Start = new DateTime(2026, 10, 25, 2, 30, 0) },
            new WallDuty { Code = "skipped", Start = new DateTime(2026, 3, 29, 2, 30, 0) });
        await wall.SaveChangesAsync(Cancellation);
        var before = wall.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var after = today.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var operations = today.GetService<IMigrationsModelDiffer>().GetDifferences(before, after);

        var commands = today.GetService<IMigrationsSqlGenerator>().Generate(operations, today.Model);
        await today.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(commands, today.GetService<IRelationalConnection>(), Cancellation);
        var read = await today.Duties.OrderBy(s => s.Code).Select(s => new { s.Code, s.Start }).ToListAsync(Cancellation);

        Assert.Contains(commands, c => c.CommandText.Contains("rask_moment_utc(\"Start\")", StringComparison.Ordinal));
        Assert.Equal(
            [("repeated", Utc(2026, 10, 25, 0, 30)), ("skipped", Utc(2026, 3, 29, 1, 30)), ("winter", Utc(2026, 1, 14, 8, 0))],
            read.Select(r => (r.Code, r.Start.Instant.UtcDateTime)));
    }

#pragma warning disable RASK127 // no value-object generator runs here, so there is no Create: the record is made as a loaded row is
    private static Duty Starting(string code, DateTime utc) => new() { Code = code, Start = new DutyStart(utc) };
#pragma warning restore RASK127

    private static DateTime Utc(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    // As a Rask context reads a value object's .Value in a query.
    private DutyContext Duties()
    {
        var options = new DbContextOptionsBuilder<DutyContext>().UseRaskSqliteAt(_database.ConnectionString);
        ValueObjectQueryOptionsExtension.Add(options);
        return new DutyContext(options.Options);
    }
}
