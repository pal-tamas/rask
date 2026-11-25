using Microsoft.EntityFrameworkCore;
using Rask.Testing.Fixture.Pumps;
using Rask.Tests.Shared;

namespace Rask.Testing.Database.Tests;

// An aggregate holding an optional value object whose members are all optional, on the database a test starts: every
// column empty is none, as on the app's own database.
public sealed class OptionalValueObjectTests
{
    private static readonly DateTime Day = new(2026, 3, 4, 0, 0, 0, DateTimeKind.Unspecified);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_optional_value_object_whose_members_are_all_optional_is_saved_and_read_as_written()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var none = TankPart.For("a", null);
        var dayOnly = TankPart.For("b", CountStart.Create(Day, null));
        var both = TankPart.For("c", CountStart.Create(Day, 1200m));
        database.Context.AddRange(none, dayOnly, both);
        await database.Context.SaveChangesAsync(Ct);

        await using var db = await database.Factory.CreateDbContextAsync(Ct);
        var starts = await db.Set<TankPart>().OrderBy(p => p.Tank).Select(p => p.Start).ToListAsync(Ct);
        var opening = await db.Set<TankPart>().CountAsync(p => p.Start != null, Ct);

        Assert.Equal([null, CountStart.Create(Day, null), CountStart.Create(Day, 1200m)], starts);
        Assert.Equal(2, opening);
    }

    [Fact]
    public async Task Taking_the_start_away_reads_back_as_none()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var part = TankPart.For("a", CountStart.Create(null, 5m));
        database.Context.Add(part);
        await database.Context.SaveChangesAsync(Ct);

        part.Opens(null);
        await database.Context.SaveChangesAsync(Ct);

        await using var db = await database.Factory.CreateDbContextAsync(Ct);
        Assert.Null(await db.Set<TankPart>().Select(p => p.Start).SingleAsync(Ct));
    }
}

// The same on a plain UseSqlite, which Rask does not configure: the app's context still holds the value.
public sealed class PlainSqliteOptionalValueObjectTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_optional_value_object_whose_members_are_all_optional_is_held_on_a_plain_UseSqlite_too()
    {
        await using var memory = new InMemorySqlite();
        await using var database = await TestDatabase.Start<PumpsContext>(o => o.UseSqlite(memory.ConnectionString), cancellationToken: Ct);
        database.Context.AddRange(TankPart.For("a", null), TankPart.For("b", CountStart.Create(null, 5m)));
        await database.Context.SaveChangesAsync(Ct);

        await using var db = await database.Factory.CreateDbContextAsync(Ct);
        var starts = await db.Set<TankPart>().OrderBy(p => p.Tank).Select(p => p.Start).ToListAsync(Ct);

        Assert.Equal([null, CountStart.Create(null, 5m)], starts);
    }
}
