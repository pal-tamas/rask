using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

public sealed class Leg
{
    public int Id { get; set; }

    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    public DateTime? Returned { get; set; }

    public DateTimeOffset Departed { get; set; }

    public DateTimeOffset Arrived { get; set; }
}

public sealed class LegDbContext(DbContextOptions<LegDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_e2e_duration";

    public DbSet<Leg> Legs => Set<Leg>();

    // The ids are the test's own, so the order a test expects reads off the seed.
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(Schema).Entity<Leg>().Property(l => l.Id).ValueGeneratedNever();
}

/// <summary>
/// <c>end - start</c> between two dates, in a query, giving the same answers on every database: PostgreSQL translates it
/// itself, and Rask writes it on SQL Server and SQLite.
/// </summary>
public abstract class DurationQueryTests : IAsyncLifetime
{
    private static readonly DateTime Noon = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Whether the database is there to run on; a test is skipped without it.</summary>
    protected abstract bool Available { get; }

    /// <summary>Why a test is skipped when it is not.</summary>
    protected abstract string SkipReason { get; }

    /// <summary>
    /// Whether a <see cref="DateTimeOffset" /> keeps its offset: PostgreSQL stores one in UTC and takes no other, so the
    /// legs there are seeded at the same instants in UTC.
    /// </summary>
    protected virtual bool KeepsOffsets => true;

    public async ValueTask InitializeAsync()
    {
        if (!Available)
        {
            return;
        }

        await using var db = Open();
        await Reset(db);
        db.Legs.AddRange(
            Made(1, Noon.AddHours(2), returned: null, At(Noon, 2), At(Noon, 0)),
            Made(2, Noon.AddMinutes(30).AddMilliseconds(250), Noon.AddDays(1), At(Noon, 0), At(Noon.AddMinutes(30), 0)),
            Made(3, Noon.AddDays(3), Noon.AddHours(1), At(Noon, 0), At(Noon, -5)));
        await db.SaveChangesAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (Available)
        {
            await using var db = Open();
            await Drop(db);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Legs_order_by_how_long_they_took()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = Open();

        var ids = await db.Legs.OrderBy(t => t.End - t.Start).Select(t => t.Id).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2, 1, 3], ids);
    }

    [Fact]
    public async Task A_duration_compares_with_a_time_span_from_the_caller()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = Open();
        var limit = TimeSpan.FromMinutes(90);

        var ids = await db.Legs.Where(t => t.End - t.Start > limit).OrderBy(t => t.Id).Select(t => t.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 3], ids);
    }

    [Fact]
    public async Task A_duration_s_total_hours_are_read_in_the_database()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = Open();

        var hours = await db.Legs.OrderBy(t => t.Id).Select(t => (t.End - t.Start).TotalHours)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2, 0.5 + (0.25 / 3600), 72], hours, (a, b) => Math.Abs(a - b) < 1e-9);
    }

    [Fact]
    public async Task Moments_in_different_offsets_are_measured_in_UTC()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = Open();

        var minutes = await db.Legs.OrderBy(t => t.Id).Select(t => (t.Arrived - t.Departed).TotalMinutes)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([120, 30, 300], minutes, (a, b) => Math.Abs(a - b) < 1e-9);
    }

    [Fact]
    public async Task A_missing_end_has_no_duration_and_matches_no_comparison()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = Open();

        var ids = await db.Legs.Where(t => t.Returned - t.Start < TimeSpan.FromDays(2)).OrderBy(t => t.Id).Select(t => t.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2, 3], ids);
    }

    /// <summary>A context on the database under test.</summary>
    protected abstract LegDbContext Open();

    /// <summary>Gives the database the legs table, empty.</summary>
    protected virtual async Task Reset(LegDbContext db)
    {
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>Takes the database away again.</summary>
    protected virtual Task Drop(LegDbContext db) => db.Database.EnsureDeletedAsync();

    private static Leg Made(int id, DateTime end, DateTime? returned, DateTimeOffset departed, DateTimeOffset arrived) =>
        new() { Id = id, Start = Noon, End = end, Returned = returned, Departed = departed, Arrived = arrived };

    // The wall time `local` in the offset `hours` — the same instant in UTC where the database keeps no offset.
    private DateTimeOffset At(DateTime local, int hours)
    {
        var moment = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromHours(hours));
        return KeepsOffsets ? moment : moment.ToUniversalTime();
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDurationQueryTests : DurationQueryTests
{
    protected override bool Available => SqlServer.Available;

    protected override string SkipReason => SqlServer.SkipReason;

    protected override LegDbContext Open() =>
        new(new DbContextOptionsBuilder<LegDbContext>().UseRaskSqlServerAt(SqlServer.Database("rask_duration")).Options);
}

[Collection(PostgresCollection.Name)]
public sealed class PostgresDurationQueryTests : DurationQueryTests
{
    protected override bool Available => Postgres.Available;

    protected override string SkipReason => Postgres.SkipReason;

    protected override bool KeepsOffsets => false;

    protected override LegDbContext Open() =>
        new(new DbContextOptionsBuilder<LegDbContext>().UseRaskPostgresAt(Postgres.Required).Options);

    protected override Task Reset(LegDbContext db) => Postgres.ResetSchemaAsync(db, LegDbContext.Schema);

    protected override Task Drop(LegDbContext db) => Postgres.DropSchemaAsync(db, LegDbContext.Schema);
}

public sealed class SqliteDurationQueryTests : DurationQueryTests
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-e2e-duration-{Guid.NewGuid():N}.db");

    protected override bool Available => true;

    protected override string SkipReason => "";

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }

    protected override LegDbContext Open() =>
        new(new DbContextOptionsBuilder<LegDbContext>().UseRaskSqliteAt($"Data Source={_path}").Options);
}
