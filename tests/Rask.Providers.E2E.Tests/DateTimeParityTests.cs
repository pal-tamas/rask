using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

public sealed class TimedRow
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public DateTime At { get; set; }

    public DateTimeOffset Stamp { get; set; }
}

/// <summary>A moment on the wall clock, as an app declares one: Rask.Data's <see cref="Moment" /> base.</summary>
public sealed partial record ParityFueled(DateTime Value) : Moment(Value);

/// <summary>A row holding a <see cref="Moment" /> value object, mapped as the generated model maps one: Rask's own converted column.</summary>
public sealed class FueledRow
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public ParityFueled Fueled { get; set; } = null!;
}

/// <summary>A date and time and a moment with its offset, as EF Core maps them on each provider.</summary>
public sealed class DateTimeParityDbContext(DbContextOptions<DateTimeParityDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_date_time_parity";

    public DbSet<TimedRow> TimedRows => Set<TimedRow>();

    public DbSet<FueledRow> FueledRows => Set<FueledRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (string.Equals(Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            modelBuilder.HasDefaultSchema(Schema);
        }

        modelBuilder.Entity<TimedRow>().HasIndex(r => r.Code).IsUnique();
        ValueObjectColumn.Map<ParityFueled, DateTime>(modelBuilder.Entity<FueledRow>().Property(r => r.Fueled), v => v.Value);
    }
}

/// <summary>
///     How a <see cref="DateTime" /> and a <see cref="DateTimeOffset" /> come back, compare and order on each provider,
///     written once: SQLite through <c>UseRaskSqlite</c> answers as SQL Server does, PostgreSQL to the microsecond.
/// </summary>
public abstract class DateTimeParityTests : IAsyncLifetime
{
    // 07.1234567: all seven of SQL Server's fractional digits, a tick below what PostgreSQL keeps.
    private static readonly DateTime Ticked = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc).AddTicks(1_234_567);

    private protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private protected abstract bool Available { get; }

    private protected abstract string SkipReason { get; }

    /// <summary>The smallest step the provider keeps: a tick (100 ns) on SQL Server and SQLite, a microsecond on PostgreSQL.</summary>
    private protected virtual long TicksKept => 1;

    /// <summary>The kind a stored <see cref="DateTime" /> comes back as.</summary>
    private protected virtual DateTimeKind KindReadBack => DateTimeKind.Unspecified;

    /// <summary>
    ///     Whether a <see cref="DateTimeOffset" /> keeps the offset it was written in. PostgreSQL's <c>timestamptz</c> takes
    ///     one in UTC alone (Npgsql refuses any other offset) and hands it back in UTC.
    /// </summary>
    private protected virtual bool KeepsTheOffset => true;

    private protected abstract DateTimeParityDbContext NewContext();

    private protected abstract Task Recreate(DbContext db);

    private protected abstract Task Drop(DbContext db);

    public async ValueTask InitializeAsync()
    {
        if (!Available)
        {
            return;
        }

        await using var db = NewContext();
        await Recreate(db);
        db.TimedRows.AddRange(
            Row("ticked", Ticked, At(9, 0, 0, TimeSpan.Zero)),
            Row("tenth", Second.AddMilliseconds(100), At(10, 0, 0, TimeSpan.FromHours(2))),
            Row("twelve", Second.AddMilliseconds(120), At(8, 30, 0, TimeSpan.FromHours(-1))),
            Row("nine", Second.AddMilliseconds(900), At(9, 15, 0, TimeSpan.FromHours(5.5))),
            Row("whole", Second, At(7, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (Available)
        {
            await using var db = NewContext();
            await Drop(db);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_date_and_time_comes_back_to_the_tick_the_provider_keeps()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();

        var read = await db.TimedRows.Where(r => r.Code == "ticked").Select(r => r.At).SingleAsync(Ct);

        Assert.Equal(Ticked.Ticks - (Ticked.Ticks % TicksKept), read.Ticks);
        Assert.Equal(KindReadBack, read.Kind);
    }

    [Fact]
    public async Task Dates_that_differ_below_a_second_order_by_their_time()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();

        var ordered = await db.TimedRows.Where(r => r.Code != "ticked").OrderBy(r => r.At).Select(r => r.Code).ToListAsync(Ct);
        var descending = await db.TimedRows.Where(r => r.Code != "ticked").OrderByDescending(r => r.At).Select(r => r.Code).ToListAsync(Ct);

        Assert.Equal(["whole", "tenth", "twelve", "nine"], ordered);
        Assert.Equal(["nine", "twelve", "tenth", "whole"], descending);
    }

    [Fact]
    public async Task A_stored_date_is_found_by_equality_and_by_range()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var stored = await db.TimedRows.Where(r => r.Code == "twelve").Select(r => r.At).SingleAsync(Ct);
        var from = Second.AddMilliseconds(100);
        var until = Second.AddMilliseconds(900);

        var equal = await db.TimedRows.Where(r => r.At == stored).Select(r => r.Code).ToListAsync(Ct);
        var between = await db.TimedRows.Where(r => r.At > from && r.At < until).Select(r => r.Code).ToListAsync(Ct);

        Assert.Equal(["twelve"], equal);
        Assert.Equal(["twelve"], between);
    }

    [Fact]
    public async Task Moments_in_different_offsets_order_by_the_instant()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();

        var ordered = await db.TimedRows.OrderBy(r => r.Stamp).Select(r => r.Code).ToListAsync(Ct);
        var latest = await db.TimedRows.MaxAsync(r => r.Stamp, Ct);

        // 07:00Z, 08:00Z (10:00+02:00), 09:00Z, 09:30Z (08:30-01:00); 09:15+05:30 is 03:45Z.
        Assert.Equal(["nine", "whole", "tenth", "ticked", "twelve"], ordered);
        Assert.Equal(At(8, 30, 0, TimeSpan.FromHours(-1)).UtcTicks, latest.UtcTicks);
    }

    [Fact]
    public async Task The_same_instant_in_another_offset_is_the_same_moment()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var nineUtcInBudapest = AsWritten(At(10, 0, 0, TimeSpan.FromHours(1)));
        var after = AsWritten(At(9, 0, 0, TimeSpan.FromHours(1)));

        var equal = await db.TimedRows.Where(r => r.Stamp == nineUtcInBudapest).Select(r => r.Code).ToListAsync(Ct);
        var later = await db.TimedRows.Where(r => r.Stamp > after).OrderBy(r => r.Code).Select(r => r.Code).ToListAsync(Ct);

        Assert.Equal(["ticked"], equal);
        Assert.Equal(["ticked", "twelve"], later);
    }

    [Fact]
    public async Task A_moment_comes_back_as_the_same_instant()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var written = At(8, 30, 0, TimeSpan.FromHours(-1)).AddTicks(1_234_567);
        db.TimedRows.Add(Row("precise", Ticked, written));
        await db.SaveChangesAsync(Ct);

        await using var fresh = NewContext();
        var read = await fresh.TimedRows.Where(r => r.Code == "precise").Select(r => r.Stamp).SingleAsync(Ct);

        Assert.Equal(written.UtcTicks - (written.UtcTicks % TicksKept), read.UtcTicks);
        Assert.Equal(KeepsTheOffset ? written.Offset : TimeSpan.Zero, read.Offset);
    }

    [Fact]
    public async Task A_wall_clock_moment_comes_back_as_the_database_keeps_it_and_orders_by_its_time()
    {
        Assert.SkipUnless(Available, SkipReason);
        var noon = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Unspecified);
        await using (var db = NewContext())
        {
            db.FueledRows.AddRange(
                Fueled("ticked", noon.AddTicks(1_234_567)),
                Fueled("later", noon.AddMilliseconds(900)),
                Fueled("first", noon));
            await db.SaveChangesAsync(Ct);
        }

        await using var fresh = NewContext();
        var read = await fresh.FueledRows.Where(r => r.Code == "ticked").Select(r => r.Fueled).SingleAsync(Ct);
        var ordered = await fresh.FueledRows.OrderBy(r => r.Fueled).Select(r => r.Code).ToListAsync(Ct);

        Assert.Equal(noon.AddTicks(1_234_567 - (1_234_567 % TicksKept)), read.Value);
        Assert.Equal(DateTimeKind.Unspecified, read.Value.Kind);
        Assert.Equal(["first", "ticked", "later"], ordered);
    }

    private protected static FueledRow Fueled(string code, DateTime at) => new() { Code = code, Fueled = ParityFueled.Create(at) };

    private static DateTime Second => new(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);

    private static DateTimeOffset At(int hour, int minute, int second, TimeSpan offset) =>
        new(2026, 3, 4, hour, minute, second, offset);

    // Npgsql refuses a moment in any offset but UTC for timestamptz — a column value or a query parameter — so there the
    // same instant is written in UTC.
    private DateTimeOffset AsWritten(DateTimeOffset moment) => KeepsTheOffset ? moment : moment.ToUniversalTime();

    private TimedRow Row(string code, DateTime at, DateTimeOffset stamp) =>
        new() { Code = code, At = at, Stamp = AsWritten(stamp) };
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDateTimeParityTests : DateTimeParityTests
{
    private const string DatabaseName = "rask_e2e_date_time_parity";

    private protected override bool Available => SqlServer.Available;

    private protected override string SkipReason => SqlServer.SkipReason;

    private protected override DateTimeParityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<DateTimeParityDbContext>().UseRaskSqlServerAt(SqlServer.Database(DatabaseName)).Options);

    private protected override async Task Recreate(DbContext db)
    {
        await db.Database.EnsureDeletedAsync(Ct);
        await db.Database.EnsureCreatedAsync(Ct);
    }

    private protected override Task Drop(DbContext db) => db.Database.EnsureDeletedAsync();
}

public sealed class SqliteDateTimeParityTests : DateTimeParityTests
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-e2e-date-time-{Guid.NewGuid():N}.db");

    private protected override bool Available => true;

    private protected override string SkipReason => "";

    private protected override DateTimeParityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<DateTimeParityDbContext>().UseRaskSqliteAt($"Data Source={_path}").Options);

    private protected override Task Recreate(DbContext db) => db.Database.EnsureCreatedAsync(Ct);

    private protected override Task Drop(DbContext db)
    {
        SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
        File.Delete(_path);
        return Task.CompletedTask;
    }
}

/// <summary>
///     PostgreSQL keeps a microsecond, hands a <see cref="DateTime" /> back as UTC and a <see cref="DateTimeOffset" /> in
///     UTC — the gaps docs/data.md names.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresDateTimeParityTests : DateTimeParityTests
{
    private protected override bool Available => Postgres.Available;

    private protected override string SkipReason => Postgres.SkipReason;

    private protected override long TicksKept => TimeSpan.TicksPerMicrosecond;

    private protected override DateTimeKind KindReadBack => DateTimeKind.Utc;

    private protected override bool KeepsTheOffset => false;

    [Fact]
    public async Task A_wall_clock_moment_is_stored_as_the_UTC_instant_it_is_and_queried_by_its_value()
    {
        Assert.SkipUnless(Available, SkipReason);
        var noon = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Unspecified);
        await using (var db = NewContext())
        {
            db.FueledRows.Add(Fueled("noon", noon));
            await db.SaveChangesAsync(Ct);
        }

        await using var fresh = NewContext();
        var stored = await fresh.Database
            .SqlQueryRaw<DateTime>($"SELECT \"Fueled\" AS \"Value\" FROM {DateTimeParityDbContext.Schema}.\"FueledRows\"")
            .SingleAsync(Ct);
        var later = await fresh.FueledRows.CountAsync(r => r.Fueled.Value > noon.AddMinutes(-1), Ct);

        Assert.Equal(TimeZoneInfo.ConvertTimeToUtc(noon, Clock.Zone), stored);
        Assert.Equal(1, later);
    }

    [Fact]
    public async Task A_moment_in_another_offset_than_UTC_is_refused()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var budapest = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.FromHours(1));

        var refused = await Assert.ThrowsAsync<ArgumentException>(
            () => db.TimedRows.Where(r => r.Stamp == budapest).ToListAsync(Ct));

        Assert.Contains("only offset 0 (UTC) is supported", refused.Message, StringComparison.Ordinal);
    }

    private protected override DateTimeParityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<DateTimeParityDbContext>().UseRaskPostgresAt(Postgres.Required).Options);

    private protected override Task Recreate(DbContext db) => Postgres.ResetSchemaAsync(db, DateTimeParityDbContext.Schema);

    private protected override Task Drop(DbContext db) => Postgres.DropSchemaAsync(db, DateTimeParityDbContext.Schema);
}
