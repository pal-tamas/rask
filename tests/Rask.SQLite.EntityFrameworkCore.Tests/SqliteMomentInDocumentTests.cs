using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

/// <summary>What a shift's log says about it, kept whole in one JSON column.</summary>
public sealed class DutyLog
{
    public DutyStart Opened { get; set; } = null!;

    public string Note { get; set; } = "";
}

public sealed class LoggedDuty
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public DutyLog Log { get; set; } = new();
}

/// <summary>A moment inside a JSON document, as <c>b.Json(d =&gt; d.Log)</c> keeps one.</summary>
public sealed class LoggedDutyContext(DbContextOptions<LoggedDutyContext> options) : DbContext(options)
{
    public DbSet<LoggedDuty> Duties => Set<LoggedDuty>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<LoggedDuty>().ComplexProperty(d => d.Log, log =>
        {
            log.ToJson();
            ValueObjectColumn.Map<DutyStart, DateTime>(log.Property(l => l.Opened), v => v.Value);
        });
}

// UseRaskSqlite keeps a Moment inside a JSON document as the instant it holds, as it does a column — without a
// migration: a document written while it held the wall clock still reads right, and holds the instant once saved again.
public sealed class SqliteMomentInDocumentTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    private readonly InMemorySqlite _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var context = Duties();
        await context.Database.EnsureCreatedAsync(Cancellation);
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Both_occurrences_of_the_repeated_hour_round_trip_through_a_document_as_their_instants()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using (var writing = Duties())
        {
            writing.Duties.AddRange(Logged("first-0230", Utc(2026, 10, 25, 0, 30)), Logged("second-0230", Utc(2026, 10, 25, 1, 30)));
            await writing.SaveChangesAsync(Cancellation);
        }

        await using var reading = Duties();
        var stored = await reading.Database.SqlQueryRaw<string>("SELECT \"Log\" AS \"Value\" FROM \"Duties\" WHERE \"Code\" = 'first-0230'")
            .SingleAsync(Cancellation);
        var read = await reading.Duties.OrderBy(d => d.Code).Select(d => d.Log.Opened).ToListAsync(Cancellation);

        Assert.Contains("\"2026-10-25T00:30:00Z\"", stored, StringComparison.Ordinal);
        Assert.Equal([Utc(2026, 10, 25, 0, 30), Utc(2026, 10, 25, 1, 30)], read.Select(r => r.Instant.UtcDateTime));
        Assert.Equal([new DateTime(2026, 10, 25, 2, 30, 0), new DateTime(2026, 10, 25, 2, 30, 0)], read.Select(r => r.Value));
    }

    [Theory]
    [InlineData("2026-07-08 12:00:00")] // as EF Core's SQLite JSON writer wrote a DateTime
    [InlineData("2026-07-08T12:00:00")] // as SQL Server's did
    public async Task A_document_written_with_the_wall_clock_reads_as_that_wall_time_and_holds_the_instant_once_saved(string wall)
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using var context = Duties();
        var document = "{\"Note\":\"before\",\"Opened\":\"" + wall + "\"}";
        await context.Database.ExecuteSqlAsync($"INSERT INTO \"Duties\" (\"Code\", \"Log\") VALUES ('old', {document})", Cancellation);

        var old = await context.Duties.SingleAsync(d => d.Code == "old", Cancellation);
        old.Log = new DutyLog { Opened = old.Log.Opened, Note = "after" };
        await context.SaveChangesAsync(Cancellation);
        var stored = await context.Database.SqlQueryRaw<string>("SELECT \"Log\" AS \"Value\" FROM \"Duties\" WHERE \"Code\" = 'old'")
            .SingleAsync(Cancellation);

        Assert.Equal(new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero), old.Log.Opened.Instant);
        Assert.Contains("\"2026-07-08T10:00:00Z\"", stored, StringComparison.Ordinal);
    }

#pragma warning disable RASK127 // no value-object generator runs here, so there is no Create: the record is made as a loaded row is
    private static LoggedDuty Logged(string code, DateTime utc) => new() { Code = code, Log = new DutyLog { Opened = new DutyStart(utc), Note = code } };
#pragma warning restore RASK127

    private static DateTime Utc(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    // As a Rask context reads a value object's .Value in a query: the options DutyContext is given.
    private LoggedDutyContext Duties()
    {
        var options = new DbContextOptionsBuilder<LoggedDutyContext>().UseRaskSqliteAt(_database.ConnectionString);
        ValueObjectQueryOptionsExtension.Add(options);
        return new LoggedDutyContext(options.Options);
    }
}
