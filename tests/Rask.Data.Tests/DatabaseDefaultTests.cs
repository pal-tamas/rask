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

        Assert.Contains("\"Size\" INTEGER NOT NULL DEFAULT (1000)", script, StringComparison.Ordinal);
        Assert.Contains("\"Kind\" INTEGER NOT NULL DEFAULT (1)", script, StringComparison.Ordinal);
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
    public void Every_default_is_the_SQL_a_hand_written_HasDefaultValueSql_would_have_said()
    {
        using var context = new NeutralContext(Options());

        var tank = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StorageTank))!;
        var size = tank.FindProperty(nameof(StorageTank.Size))!;

        Assert.Equal("1000", size.GetDefaultValueSql());
        Assert.False(size.TryGetDefaultValue(out _));
    }

    [Fact]
    public void A_stored_default_the_value_object_refuses_is_written_as_the_column_stores_it()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Number\" INTEGER NOT NULL DEFAULT (0)", script, StringComparison.Ordinal);
        Assert.Contains("\"Locked\" INTEGER NOT NULL DEFAULT (0)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stored_default_of_another_type_than_the_column_s_fails_the_model_build_naming_it()
    {
        using var context = RefusedContext.Declaring(m => m.Entity<PumpNozzle>(b =>
        {
            ValueObjectColumn.Map<NozzleNumber, int>(b.Property(n => n.Number), v => v.Value);
            ValueObjectColumn.Map<NozzleSent, DateTime>(b.Property(n => n.Sent), v => v.Value);
            b.StoredDefault(n => n.Number, "0");
        }));

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("StoredDefault(x => x.Number, 0)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("stores a Int32, and the default is a String", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_wall_clock_is_the_database_s_local_time_for_a_date_time_and_a_value_object_over_one()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Sent\" TEXT NOT NULL DEFAULT (strftime('%Y-%m-%d %H:%M:%f', 'now', 'localtime'))", script, StringComparison.Ordinal);
        Assert.Contains("\"SeenAt\" TEXT NOT NULL DEFAULT (strftime('%Y-%m-%d %H:%M:%f', 'now', 'localtime'))", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_written_without_a_number_reads_back_the_legacy_zero_and_the_local_time()
    {
        await using var connection = await Open();
        await using var context = new NeutralContext(Options(connection));
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var before = DateTime.Now.AddMinutes(-1);

        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"PumpNozzle\" (\"Id\") VALUES ('7d3f4b1e-0000-0000-0000-000000000002')", TestContext.Current.CancellationToken);

        var row = await context.Database.SqlQueryRaw<int>("SELECT \"Number\" AS \"Value\" FROM \"PumpNozzle\"")
            .SingleAsync(TestContext.Current.CancellationToken);
        var sent = await context.Database.SqlQueryRaw<DateTime>("SELECT \"SeenAt\" AS \"Value\" FROM \"PumpNozzle\"")
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, row);
        Assert.InRange(sent, before, DateTime.Now.AddMinutes(1));
    }

    [Fact]
    public void The_wall_clock_is_refused_for_a_moment_with_an_offset()
    {
        using var context = RefusedContext.Declaring(m => m.Entity<StorageTank>().Default(t => t.CreatedAt, () => Clock.Wall));

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("a wall-clock time has no offset", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Today_is_refused_since_the_database_keeps_no_calendar_day()
    {
        var tank = new ModelBuilder().Entity<StorageTank>();

        var refused = Assert.Throws<ArgumentException>(() => tank.Default(t => t.CountedAt, () => Clock.Today));

        Assert.Contains("() => Clock.Wall", refused.Message, StringComparison.Ordinal);
    }

    private static async Task<SqliteConnection> Open()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static DbContextOptions<NeutralContext> Options() =>
        OptionsLayout.LikeAddDbContext<NeutralContext>().UseSqlite("Data Source=:memory:").Options;

    private static DbContextOptions<NeutralContext> Options(SqliteConnection connection) =>
        OptionsLayout.LikeAddDbContext<NeutralContext>().UseSqlite(connection).Options;
}
