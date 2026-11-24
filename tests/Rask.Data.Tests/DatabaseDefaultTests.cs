using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

/// <summary><c>Default(…)</c> on SQLite: the database's default, for the rows Rask does not write.</summary>
[Collection(DataDbCollection.Name)]
public sealed class DatabaseDefaultTests
{
    [Fact]
    public void A_constant_default_and_the_clock_are_column_defaults_in_the_schema()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Size\" INTEGER NOT NULL DEFAULT 1000", script, StringComparison.Ordinal);
        Assert.Contains("\"Kind\" INTEGER NOT NULL DEFAULT 1", script, StringComparison.Ordinal);
        Assert.Contains("\"CreatedAt\" TEXT NOT NULL DEFAULT (strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00')", script, StringComparison.Ordinal);
        Assert.Contains("\"CountedAt\" TEXT NOT NULL DEFAULT (strftime('%Y-%m-%d %H:%M:%f', 'now'))", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Rask_always_writes_the_value_the_entity_holds()
    {
        using var context = new NeutralContext(Options());

        var tank = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StorageTank))!;

        Assert.All(
            [nameof(StorageTank.Size), nameof(StorageTank.Kind), nameof(StorageTank.CreatedAt), nameof(StorageTank.CountedAt)],
            name => Assert.Equal(ValueGenerated.Never, tank.FindProperty(name)!.ValueGenerated));
    }

    [Fact]
    public async Task A_size_of_nothing_is_saved_as_nothing_not_as_the_default()
    {
        await using var connection = await Open();
        await using var context = new NeutralContext(Options(connection));
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var tank = new StorageTank { Name = "empty", Size = 0, Kind = PumpKind.Manual, CreatedAt = Clock.Now };

        context.Add(tank);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var read = await context.Set<StorageTank>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, read.Size);
        Assert.Equal(PumpKind.Manual, read.Kind);
    }

    [Fact]
    public async Task A_row_written_without_the_columns_takes_the_defaults_and_the_database_s_clock()
    {
        await using var connection = await Open();
        await using var context = new NeutralContext(Options(connection));
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);

        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"StorageTank\" (\"Id\", \"Name\") VALUES ('7d3f4b1e-0000-0000-0000-000000000001', 'legacy')",
            TestContext.Current.CancellationToken);

        var read = await context.Set<StorageTank>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1000, read.Size);
        Assert.Equal(PumpKind.Pump, read.Kind);
        Assert.InRange(read.CreatedAt, before, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(TimeSpan.Zero, read.CreatedAt.Offset);
        Assert.InRange(read.CountedAt, before.UtcDateTime, DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void Another_lambda_is_refused_where_it_is_written()
    {
        var tank = new ModelBuilder().Entity<StorageTank>();

        var refused = Assert.Throws<ArgumentException>(() => tank.Default(t => t.CreatedAt, () => DateTimeOffset.Now));

        Assert.Equal("now", refused.ParamName);
        Assert.Contains("() => Clock.Now", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_wall_clock_is_refused_since_the_database_does_not_know_the_app_s_zone()
    {
        var tank = new ModelBuilder().Entity<StorageTank>();

        var refused = Assert.Throws<ArgumentException>(() => tank.Default(t => t.CountedAt, () => Clock.Wall));

        Assert.Contains("the database does not know the app's time zone", refused.Message, StringComparison.Ordinal);
    }

    private static async Task<SqliteConnection> Open()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static DbContextOptions<NeutralContext> Options() =>
        new DbContextOptionsBuilder<NeutralContext>().UseSqlite("Data Source=:memory:").Options;

    private static DbContextOptions<NeutralContext> Options(SqliteConnection connection) =>
        new DbContextOptionsBuilder<NeutralContext>().UseSqlite(connection).Options;
}
