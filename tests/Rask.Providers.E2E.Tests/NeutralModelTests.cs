using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Providers.E2E.Tests;

public enum GaugeKind
{
    Manual,
    Pump,
}

/// <summary>A reading whose computed columns each say one thing the C# subset can.</summary>
public sealed class Gauge
{
    public static readonly Expression<Func<Gauge, int>> CountedAtIs = g => g.ModifiedPistol ?? g.Pistol;
    public static readonly Expression<Func<Gauge, string>> NameIs = g => g.Year + "." + g.Month;
    public static readonly Expression<Func<Gauge, string>> LabelIs = g => g.Note + "#" + g.ModifiedPistol;
    public static readonly Expression<Func<Gauge, bool>> MovedIs = g => g.ModifiedPistol != null && g.ModifiedPistol != g.Pistol;
    public static readonly Expression<Func<Gauge, bool>> SamePistolIs = g => g.ModifiedPistol == g.Pistol;
    public static readonly Expression<Func<Gauge, int>> SpreadIs = g => Math.Abs(g.Pistol - g.ModifiedPistol.GetValueOrDefault()) % 7;
    public static readonly Expression<Func<Gauge, string>> SizeIs = g => g.Litres * 2 > 100 ? "big" : g.Kind == GaugeKind.Pump ? "pump" : "small";

    public Guid Id { get; set; }

    public int Pistol { get; set; }

    public int? ModifiedPistol { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public string? Note { get; set; }

    public double Litres { get; set; }

    public GaugeKind Kind { get; set; }

    public int CountedAt { get; private set; }

    public string Name { get; private set; } = "";

    public string Label { get; private set; } = "";

    public bool Moved { get; private set; }

    public bool SamePistol { get; private set; }

    public int Spread { get; private set; }

    public string Size { get; private set; } = "";
}

/// <summary>A partner's grant: one live per partner, any number revoked.</summary>
public sealed class Licence
{
    public Guid Id { get; set; }

    public string? Partner { get; set; }

    public DateTime? RevokedAt { get; set; }
}

/// <summary>A tank the database defaults for rows written without its columns.</summary>
public sealed class Cistern
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public int Size { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTime CountedAt { get; set; }
}

/// <summary>The three provider-neutral declarations, under Rask's conventions.</summary>
public sealed class NeutralDbContext(DbContextOptions<NeutralDbContext> options) : DbContext(options)
{
    public const string Schema = "neutral";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        modelBuilder.Entity<Gauge>(b =>
        {
            b.Property(g => g.Note).HasMaxLength(100);
            b.Computed(g => g.CountedAt, Gauge.CountedAtIs);
            b.Computed(g => g.Name, Gauge.NameIs).HasMaxLength(50);
            b.Computed(g => g.Label, Gauge.LabelIs).HasMaxLength(150);
            b.Computed(g => g.Moved, Gauge.MovedIs);
            b.Computed(g => g.SamePistol, Gauge.SamePistolIs);
            b.Computed(g => g.Spread, Gauge.SpreadIs);
            b.Computed(g => g.Size, Gauge.SizeIs).HasMaxLength(10);
        });
        modelBuilder.Entity<Licence>(b =>
        {
            b.Property(l => l.Partner).HasMaxLength(50);
            b.HasIndex(l => l.Partner).IsUnique().Where(l => l.RevokedAt == null);
        });
        modelBuilder.Entity<Cistern>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(50);
            b.Default(c => c.Size, 1000);
            b.Default(c => c.CreatedAt, () => Clock.Now);
            b.Default(c => c.CountedAt, () => Clock.Now.UtcDateTime);
        });
    }
}

/// <summary>What every provider is held to: the same rows, the same answers, as the C# gives them.</summary>
internal static class NeutralModelParity
{
    private static readonly Gauge[] Rows =
    [
        new() { Pistol = 3, ModifiedPistol = null, Year = 2026, Month = 1, Note = null, Litres = 10, Kind = GaugeKind.Pump },
        new() { Pistol = 3, ModifiedPistol = 3, Year = 2025, Month = 12, Note = "same", Litres = 60, Kind = GaugeKind.Manual },
        new() { Pistol = 2, ModifiedPistol = 19, Year = 1999, Month = 7, Note = "", Litres = 49.5, Kind = GaugeKind.Manual },
        new() { Pistol = -4, ModifiedPistol = 0, Year = 0, Month = -1, Note = "x", Litres = 0, Kind = GaugeKind.Pump },
    ];

    internal static async Task ComputedColumnsAgreeWithCSharp(NeutralDbContext db, CancellationToken cancellationToken)
    {
        db.AddRange(Rows);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var read = await db.Set<Gauge>().AsNoTracking().ToDictionaryAsync(g => g.Id, cancellationToken);
        foreach (var row in Rows)
        {
            var stored = read[row.Id];
            Assert.Equal(Gauge.CountedAtIs.Compile()(row), stored.CountedAt);
            Assert.Equal(Gauge.NameIs.Compile()(row), stored.Name);
            Assert.Equal(Gauge.LabelIs.Compile()(row), stored.Label);
            Assert.Equal(Gauge.MovedIs.Compile()(row), stored.Moved);
            Assert.Equal(Gauge.SamePistolIs.Compile()(row), stored.SamePistol);
            Assert.Equal(Gauge.SpreadIs.Compile()(row), stored.Spread);
            Assert.Equal(Gauge.SizeIs.Compile()(row), stored.Size);
        }
    }

    internal static async Task FilteredUniqueHoldsAmongTheKeptRows(NeutralDbContext db, CancellationToken cancellationToken)
    {
        var revoked = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.AddRange(
            new Licence { Partner = "p1", RevokedAt = revoked },
            new Licence { Partner = "p1", RevokedAt = revoked },
            new Licence { Partner = "p1" },
            new Licence { Partner = null },
            new Licence { Partner = null });
        await db.SaveChangesAsync(cancellationToken);

        db.Add(new Licence { Partner = "p1" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    internal static async Task DefaultsFillARowWrittenWithoutThem(NeutralDbContext db, CancellationToken cancellationToken)
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-5);
        var helper = db.GetService<ISqlGenerationHelper>();
        var table = helper.DelimitIdentifier("Cistern", db.Database.IsSqlite() ? null : NeutralDbContext.Schema);

#pragma warning disable EF1002 // The table and column names are constants of this test, quoted by the provider; the values are parameters.
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {table} ({helper.DelimitIdentifier("Id")}, {helper.DelimitIdentifier("Name")}) VALUES ({{0}}, {{1}})",
            [Guid.NewGuid(), "legacy"],
            cancellationToken);
#pragma warning restore EF1002

        var read = await db.Set<Cistern>().AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(1000, read.Size);
        Assert.InRange(read.CreatedAt, before, DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.InRange(read.CountedAt, before.UtcDateTime, DateTime.UtcNow.AddMinutes(5));
    }
}

/// <summary>The declarations on SQLite: generated columns, a partial index, column defaults.</summary>
public sealed class SqliteNeutralModelTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private NeutralDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new NeutralDbContext(new DbContextOptionsBuilder<NeutralDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public Task Computed_columns_agree_with_the_C_sharp() =>
        NeutralModelParity.ComputedColumnsAgreeWithCSharp(_db, TestContext.Current.CancellationToken);

    [Fact]
    public Task A_filtered_unique_index_holds_among_the_rows_it_keeps() =>
        NeutralModelParity.FilteredUniqueHoldsAmongTheKeptRows(_db, TestContext.Current.CancellationToken);

    [Fact]
    public Task Defaults_fill_a_row_written_without_them() =>
        NeutralModelParity.DefaultsFillARowWrittenWithoutThem(_db, TestContext.Current.CancellationToken);
}

/// <summary>The declarations on PostgreSQL: stored generated columns, a partial index, column defaults.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresNeutralModelTests
{
    [Fact]
    public async Task Computed_columns_agree_with_the_C_sharp()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var db = await Fresh();

        await NeutralModelParity.ComputedColumnsAgreeWithCSharp(db, TestContext.Current.CancellationToken);

        await Postgres.DropSchemaAsync(db, NeutralDbContext.Schema);
    }

    [Fact]
    public async Task A_filtered_unique_index_holds_among_the_rows_it_keeps()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var db = await Fresh();

        await NeutralModelParity.FilteredUniqueHoldsAmongTheKeptRows(db, TestContext.Current.CancellationToken);

        await Postgres.DropSchemaAsync(db, NeutralDbContext.Schema);
    }

    [Fact]
    public async Task Defaults_fill_a_row_written_without_them()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var db = await Fresh();

        await NeutralModelParity.DefaultsFillARowWrittenWithoutThem(db, TestContext.Current.CancellationToken);

        await Postgres.DropSchemaAsync(db, NeutralDbContext.Schema);
    }

    private static async Task<NeutralDbContext> Fresh()
    {
        var db = new NeutralDbContext(new DbContextOptionsBuilder<NeutralDbContext>().UseRaskPostgresAt(Postgres.Required).Options);
        await Postgres.ResetSchemaAsync(db, NeutralDbContext.Schema);
        return db;
    }
}

/// <summary>The declarations on SQL Server: persisted computed columns, a filtered index, column defaults.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerNeutralModelTests
{
    [Fact]
    public async Task Computed_columns_agree_with_the_C_sharp()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var db = await Fresh("rask_neutral_computed");

        await NeutralModelParity.ComputedColumnsAgreeWithCSharp(db, TestContext.Current.CancellationToken);

        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_filtered_unique_index_holds_among_the_rows_it_keeps()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var db = await Fresh("rask_neutral_filtered");

        await NeutralModelParity.FilteredUniqueHoldsAmongTheKeptRows(db, TestContext.Current.CancellationToken);

        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Defaults_fill_a_row_written_without_them()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var db = await Fresh("rask_neutral_defaults");

        await NeutralModelParity.DefaultsFillARowWrittenWithoutThem(db, TestContext.Current.CancellationToken);

        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<NeutralDbContext> Fresh(string database)
    {
        var db = new NeutralDbContext(new DbContextOptionsBuilder<NeutralDbContext>()
            .UseRaskSqlServerAt(SqlServer.Database(database)).Options);
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        return db;
    }
}
