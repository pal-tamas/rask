using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Providers.E2E.Tests;

/// <summary>A reading's moment, as an app declares one.</summary>
public sealed partial record ReadingAt(DateTime Value) : Moment(Value);

public sealed class Reading
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public ReadingAt At { get; set; } = null!;
}

/// <summary>Moments mapped as a Rask context maps them: Rask's own converted column, and its query surface.</summary>
public sealed class MomentDbContext(DbContextOptions<MomentDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_moments";

    public DbSet<Reading> Readings => Set<Reading>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => ValueObjectQueryOptionsExtension.Add(optionsBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        ValueObjectColumn.Map<ReadingAt, DateTime>(modelBuilder.Entity<Reading>().Property(r => r.At), v => v.Value);
    }
}

/// <summary>
///     A <see cref="Moment" /> is the UTC instant it holds on every provider — SQL Server's <c>datetime2</c>, SQLite's text,
///     PostgreSQL's <c>timestamptz</c> — so it is stored, ordered, compared, summed up by <c>Min</c>/<c>Max</c> and grouped
///     by its day on the app's wall calendar alike, the two <c>02:30</c>s of an October night in Budapest kept apart.
/// </summary>
public abstract class MomentParityTests(ProviderLeg leg) : ParityTests<MomentDbContext>(leg)
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    [Fact]
    public async Task A_moment_is_stored_as_the_utc_instant_it_holds()
    {
        RequireServer();
        using var zone = AmbientClock.UseZone(Budapest);
        await Seed();
        await using var db = Open();

        var sql = $"SELECT {Quoted(db, "At")} AS {Quoted(db, "Value")} FROM {Table(db)} WHERE {Quoted(db, "Code")} = 'summer-noon'";
#pragma warning disable EF1002 // the names are the test's own constants
        var stored = await db.Database.SqlQueryRaw<DateTime>(sql).SingleAsync(Ct);
#pragma warning restore EF1002
        var read = await db.Readings.Where(r => r.Code == "summer-noon").Select(r => r.At).SingleAsync(Ct);

        Assert.Equal(new DateTime(2026, 7, 8, 10, 0, 0), stored);
        Assert.Equal(new DateTime(2026, 7, 8, 12, 0, 0), read.Value);
        Assert.Equal(new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero), read.Instant);
    }

    [Fact]
    public async Task The_two_moments_of_the_repeated_hour_come_back_apart_and_order_as_they_happened()
    {
        RequireServer();
        using var zone = AmbientClock.UseZone(Budapest);
        await Seed();
        await using var db = Open();

        var ordered = await db.Readings.OrderBy(r => r.At).Select(r => r.Code).ToListAsync(Ct);
        var first = await db.Readings.SingleAsync(r => r.Code == "first-0230", Ct);
        var second = await db.Readings.SingleAsync(r => r.Code == "second-0230", Ct);

        Assert.Equal(["summer-noon", "late-evening", "first-0230", "second-0230"], ordered);
        Assert.Equal(first.At.Value, second.At.Value);
        Assert.Equal(TimeSpan.FromHours(1), second.At.Instant - first.At.Instant);
    }

    [Fact]
    public async Task Min_max_and_a_wall_time_comparison_read_the_instant()
    {
        RequireServer();
        using var zone = AmbientClock.UseZone(Budapest);
        await Seed();
        await using var db = Open();
        var since = new DateTime(2026, 10, 25, 2, 45, 0); // shown twice: its first occurrence, 00:45Z

        var earliest = await db.Readings.MinAsync(r => r.At.Value, Ct);
        var latest = await db.Readings.MaxAsync(r => r.At, Ct);
        var after = await db.Readings.Where(r => r.At.Value > since).Select(r => r.Code).ToListAsync(Ct);

        Assert.Equal(new DateTime(2026, 7, 8, 12, 0, 0), earliest);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), latest.Instant);
        Assert.Equal(["second-0230"], after);
    }

    [Fact]
    public async Task Moments_group_by_their_day_on_the_app_s_wall_calendar()
    {
        RequireServer();
        using var zone = AmbientClock.UseZone(Budapest);
        await Seed();
        await using var db = Open();

        var days = await db.Readings.GroupBy(r => r.At.Day).Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(g => g.Day).ToListAsync(Ct);
        var onTheDay = await db.Readings.CountAsync(r => r.At.Day == new DateOnly(2026, 10, 25), Ct);

        Assert.Equal([(new DateOnly(2026, 7, 8), 1), (new DateOnly(2026, 10, 25), 3)], days.Select(d => (d.Day, d.Count)));
        Assert.Equal(3, onTheDay);
    }

    [Fact]
    public async Task A_part_of_the_time_read_off_the_utc_column_is_refused()
    {
        RequireServer();
        using var zone = AmbientClock.UseZone(Budapest);
        await using var db = Open();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Readings.Where(r => r.At.Value.Hour == 2).ToListAsync(Ct));

        Assert.Contains("'.Day'", refused.Message, StringComparison.Ordinal);
    }

    private protected override MomentDbContext Open() => Leg.Open<MomentDbContext>(static o => new MomentDbContext(o));

    private static string Quoted(DbContext db, string name) => db.Database.IsSqlServer() ? $"[{name}]" : $"\"{name}\"";

    private static string Table(DbContext db) =>
        db.Database.IsSqlite() ? "\"Readings\"" : $"{Quoted(db, MomentDbContext.Schema)}.{Quoted(db, "Readings")}";

    private async Task Seed()
    {
        await using var db = Open();
        db.Readings.AddRange(
            Read("first-0230", new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc)),
            Read("second-0230", new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc)),
            Read("late-evening", new DateTime(2026, 10, 24, 22, 30, 0, DateTimeKind.Utc)),
            Read("summer-noon", new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync(Ct);
    }

    private static Reading Read(string code, DateTime utc) => new() { Code = code, At = ReadingAt.Create(utc) };
}

public sealed class SqliteMomentParityTests() : MomentParityTests(ProviderLeg.Sqlite("moments"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerMomentParityTests() : MomentParityTests(ProviderLeg.SqlServer("rask_e2e_moments"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresMomentParityTests() : MomentParityTests(ProviderLeg.Postgres(MomentDbContext.Schema));
