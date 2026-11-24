using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Rask.SqlServer.Tests;

/// <summary>
/// <c>end - start</c> between two dates, as <c>UseRaskSqlServer</c> writes it — checked as SQL, offline. The provider
/// suite runs the same queries on a real server.
/// </summary>
public sealed class DurationQueryTests
{
    [Fact]
    public void A_duration_is_ordered_by_the_microseconds_between_the_two_dates()
    {
        using var db = Open();

        var sql = db.Trips.OrderBy(t => t.End - t.Start).Select(t => t.Id).ToQueryString();

        Assert.Contains("ORDER BY DATEDIFF_BIG(microsecond, [t].[Start], [t].[End])", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_time_span_from_the_caller_is_sent_as_microseconds()
    {
        using var db = Open();
        var limit = TimeSpan.FromMinutes(90);

        var sql = db.Trips.Where(t => t.End - t.Start > limit).Select(t => t.Id).ToQueryString();

        Assert.Contains("DECLARE @limit bigint = CAST(5400000000 AS bigint);", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE DATEDIFF_BIG(microsecond, [t].[Start], [t].[End]) > @limit", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duration_s_total_hours_divide_the_microseconds_down()
    {
        using var db = Open();

        var sql = db.Trips.Select(t => (t.Arrived - t.Departed).TotalHours).ToQueryString();

        Assert.Contains(
            "SELECT CAST(DATEDIFF_BIG(microsecond, [t].[Departed], [t].[Arrived]) AS float) / 3600000000.0E0",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_nullable_end_makes_a_nullable_duration()
    {
        using var db = Open();

        var sql = db.Trips.Where(t => t.Returned - t.Start < TimeSpan.FromDays(2)).Select(t => t.Id).ToQueryString();

        Assert.Contains("WHERE DATEDIFF_BIG(microsecond, [t].[Start], [t].[Returned]) < CAST(172800000000 AS bigint)", sql, StringComparison.Ordinal);
    }

    private static TripContext Open()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>(
                    "Rask:ConnectionStrings:App", "Server=localhost;Database=rask;User Id=sa;Password=x;TrustServerCertificate=true"),
            ])
            .Build();
        return new(new DbContextOptionsBuilder<TripContext>().UseRaskSqlServer(new ConfigurationServices(configuration)).Options);
    }

    public sealed class Trip
    {
        public int Id { get; set; }

        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public DateTime? Returned { get; set; }

        public DateTimeOffset Departed { get; set; }

        public DateTimeOffset Arrived { get; set; }
    }

    private sealed class TripContext(DbContextOptions<TripContext> options) : DbContext(options)
    {
        public DbSet<Trip> Trips => Set<Trip>();
    }
}
