using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// A browser app's database: a plain UseSqlite with UseRaskFullTextSearch, no pragmas and no retry — and still the same
// text comparison and the same `end - start` as every Rask SQLite.
public sealed class PlainSqliteTests : IDisposable
{
    private readonly InMemorySqlite _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Text_compares_as_the_app_s_SQL_Server_collation_does()
    {
        await using var db = new HungarianContext(new DbContextOptionsBuilder<HungarianContext>()
            .UseSqlite(_database.ConnectionString).UseRaskFullTextSearch().Options);
        await db.Database.EnsureCreatedAsync(Ct);
        db.Drivers.AddRange(
            new Driver { Name = "Csaba", Code = "c-1", Plate = "a" },
            new Driver { Name = "Cecil", Code = "c-2", Plate = "b" },
            new Driver { Name = "Árvíztűrő", Code = "a-1", Plate = "c" });
        await db.SaveChangesAsync(Ct);

        var ordered = await db.Drivers.OrderBy(d => d.Name.InTextOrder()).Select(d => d.Name).ToListAsync(Ct);
        var found = await db.Drivers.CountAsync(d => d.Name == "CSABA" || d.Name.Contains("árv"), Ct);

        Assert.Equal(["Árvíztűrő", "Cecil", "Csaba"], ordered);
        Assert.Equal(2, found);
    }

    [Fact]
    public async Task A_duration_between_two_dates_is_read_in_the_database()
    {
        var noon = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Unspecified);
        await using var db = new TripContext(new DbContextOptionsBuilder<TripContext>()
            .UseSqlite(_database.ConnectionString).UseRaskFullTextSearch().Options);
        await db.Database.EnsureCreatedAsync(Ct);
        db.Trips.AddRange(
            new Trip { Id = 1, Start = noon, End = noon.AddHours(2) },
            new Trip { Id = 2, Start = noon, End = noon.AddMinutes(30) });
        await db.SaveChangesAsync(Ct);
        var limit = TimeSpan.FromHours(1);

        var longer = await db.Trips.Where(t => t.End - t.Start > limit).Select(t => t.Id).ToListAsync(Ct);

        Assert.Equal([1], longer);
    }
}
