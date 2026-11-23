using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

public sealed class Trip
{
    public int Id { get; set; }

    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    public DateTime? Returned { get; set; }

    public DateTimeOffset Departed { get; set; }

    public DateTimeOffset Arrived { get; set; }
}

public sealed class TripContext(DbContextOptions<TripContext> options) : DbContext(options)
{
    public DbSet<Trip> Trips => Set<Trip>();
}

/// <summary><c>end - start</c> between two dates, in a query on SQLite: a length of time EF Core's provider cannot write.</summary>
public sealed class DurationQueryTests : IAsyncLifetime
{
    private static readonly DateTime Noon = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Unspecified);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-sqlite-duration-{Guid.NewGuid():N}.db");

    public async ValueTask InitializeAsync()
    {
        await using var db = Open();
        await db.Database.EnsureCreatedAsync();
        db.Trips.AddRange(
            new Trip
            {
                Id = 1,
                Start = Noon,
                End = Noon.AddHours(2),
                Returned = null,
                Departed = new DateTimeOffset(Noon, TimeSpan.FromHours(2)),
                Arrived = new DateTimeOffset(Noon, TimeSpan.Zero),
            },
            new Trip
            {
                Id = 2,
                Start = Noon,
                End = Noon.AddMinutes(30).AddMilliseconds(250),
                Returned = Noon.AddDays(1),
                Departed = new DateTimeOffset(Noon, TimeSpan.Zero),
                Arrived = new DateTimeOffset(Noon.AddMinutes(30), TimeSpan.Zero),
            },
            new Trip
            {
                Id = 3,
                Start = Noon,
                End = Noon.AddDays(3),
                Returned = Noon.AddHours(1),
                Departed = new DateTimeOffset(Noon, TimeSpan.Zero),
                Arrived = new DateTimeOffset(Noon, TimeSpan.FromHours(-5)),
            });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void A_duration_is_written_as_whole_microseconds_between_the_two_dates()
    {
        using var db = Open();

        var sql = db.Trips.OrderBy(t => t.End - t.Start).Select(t => t.Id).ToQueryString();

        Assert.Contains(
            "ORDER BY CAST(round((unixepoch(\"t\".\"End\", 'subsec') - unixepoch(\"t\".\"Start\", 'subsec')) * 1000000.0) AS INTEGER)",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trips_order_by_how_long_they_took()
    {
        await using var db = Open();

        var ids = await db.Trips.OrderBy(t => t.End - t.Start).Select(t => t.Id).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2, 1, 3], ids);
    }

    [Fact]
    public async Task A_duration_compares_with_a_time_span_from_the_caller()
    {
        await using var db = Open();
        var limit = TimeSpan.FromMinutes(90);

        var ids = await db.Trips.Where(t => t.End - t.Start > limit).OrderBy(t => t.Id).Select(t => t.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 3], ids);
    }

    [Fact]
    public async Task A_duration_s_total_hours_are_read_in_the_database()
    {
        await using var db = Open();

        var hours = await db.Trips.OrderBy(t => t.Id).Select(t => (t.End - t.Start).TotalHours)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2, 0.5 + (0.25 / 3600), 72], hours, (a, b) => Math.Abs(a - b) < 1e-9);
    }

    [Fact]
    public async Task A_duration_reads_back_as_a_time_span_to_the_millisecond()
    {
        await using var db = Open();

        var took = await db.Trips.Where(t => t.Id == 2).Select(t => t.End - t.Start)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMinutes(30) + TimeSpan.FromMilliseconds(250), took);
    }

    [Fact]
    public async Task Moments_in_different_offsets_are_measured_in_UTC()
    {
        await using var db = Open();

        var minutes = await db.Trips.OrderBy(t => t.Id).Select(t => (t.Arrived - t.Departed).TotalMinutes)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([120, 30, 300], minutes);
    }

    [Fact]
    public async Task A_missing_end_has_no_duration_and_matches_no_comparison()
    {
        await using var db = Open();

        var ids = await db.Trips.Where(t => t.Returned - t.Start < TimeSpan.FromDays(2)).OrderBy(t => t.Id).Select(t => t.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2, 3], ids);
    }

    private TripContext Open() =>
        new(new DbContextOptionsBuilder<TripContext>().UseRaskSqliteAt($"Data Source={_path}").Options);
}
