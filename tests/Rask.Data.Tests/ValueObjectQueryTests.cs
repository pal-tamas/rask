using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
/// A value object of one value is its column in a query: its <c>.Value</c> is filtered, summed and ordered by, the
/// value object itself is compared and ordered by, and an <c>ExecuteUpdate</c> sets it.
/// </summary>
public sealed class ValueObjectQueryTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task The_value_a_value_object_holds_is_summed_and_filtered_in_the_database()
    {
        await using var database = await StartWithRigsAsync();
        var rigs = database.Context.Set<Rig>();
        var token = TestContext.Current.CancellationToken;

        var total = await rigs.SumAsync(r => r.Tank.Value, token);
        var large = await rigs.Where(r => r.Tank.Value > 15).Select(r => r.Driver.Value).ToListAsync(token);
        var named = await rigs.CountAsync(r => r.Driver.Value.StartsWith("B"), token);
        var fueledLate = await rigs.CountAsync(r => r.Fueled!.Value > new DateTime(2024, 1, 15), token);

        Assert.Equal(60.5m, total);
        Assert.Equal(["Bea", "Cid"], large.Order(StringComparer.Ordinal));
        Assert.Equal(1, named);
        Assert.Equal(2, fueledLate);
    }

    [Fact]
    public async Task A_query_orders_by_the_value_object_and_by_the_value_it_holds()
    {
        await using var database = await StartWithRigsAsync();
        var rigs = database.Context.Set<Rig>();
        var token = TestContext.Current.CancellationToken;

        var byMoment = await rigs.OrderBy(r => r.Fueled).ThenBy(r => r.Tank).Select(r => r.Driver.Value).ToListAsync(token);
        var byValue = await rigs.OrderByDescending(r => r.Tank.Value).Select(r => r.Driver.Value).ToListAsync(token);

        Assert.Equal(["Ada", "Cid", "Bea"], byMoment);
        Assert.Equal(["Cid", "Bea", "Ada"], byValue);
    }

    [Fact]
    public async Task A_value_object_is_compared_with_another_of_its_type_in_the_database()
    {
        await using var database = await StartWithRigsAsync();
        var rigs = database.Context.Set<Rig>();
        var token = TestContext.Current.CancellationToken;
        var least = RigLitres.Create(15);
        var since = RigFueled.Create(new DateTime(2024, 1, 15));

        var large = await rigs.CountAsync(r => r.Tank >= least, token);
        var late = await rigs.CountAsync(r => r.Fueled > since, token);
        var latest = await rigs.MaxAsync(r => r.Fueled, token);

        Assert.Equal(2, large);
        Assert.Equal(2, late);
        Assert.Equal(new DateTime(2024, 3, 1), latest!.Value);
    }

    [Fact]
    public async Task An_execute_update_sets_a_value_object_column()
    {
        await using var database = await StartWithRigsAsync();
        var rigs = database.Context.Set<Rig>();
        var token = TestContext.Current.CancellationToken;
        var refueled = RigFueled.Create(new DateTime(2024, 4, 1));

        var changed = await rigs.Where(r => r.Tank.Value < 15)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Fueled, refueled).SetProperty(r => r.Driver, RigDriver.Create("Dan")), token);

        Assert.Equal(1, changed);
        var stored = await rigs.AsNoTracking().SingleAsync(r => r.Driver.Value == "Dan", token);
        Assert.Equal(new DateTime(2024, 4, 1), stored.Fueled!.Value);
    }

    [Fact]
    public async Task The_value_of_a_value_object_inside_a_complex_one_is_read_in_the_database()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        await Gondola.Packed("AB-1", weight: 12).Save(cancellationToken: token);
        await Gondola.Packed("CD-2", weight: 30).Save(cancellationToken: token);
        await Haul.Of("Ada", 10, 20).Save(cancellationToken: token);
        var gondolas = database.Context.Set<Gondola>();

        var heavy = await gondolas.Where(g => g.Crate.Weight.Value > 20).Select(g => g.Crate.Code.Value).ToListAsync(token);
        var weight = await gondolas.SumAsync(g => g.Crate.Weight.Value, token);
        var km = await database.Context.Set<Haul>().SumAsync(h => h.Km.End - h.Km.Start, token);

        Assert.Equal(["CD-2"], heavy);
        Assert.Equal(42, weight);
        Assert.Equal(10, km);
    }

    [Fact]
    public void A_value_object_of_one_value_orders_like_its_value_outside_the_database()
    {
        var litres = new[] { RigLitres.Create(5), RigLitres.Create(2), RigLitres.Create(9) };

        var sorted = litres.Order().Select(l => l.Value).ToList();

        Assert.Equal([2m, 5m, 9m], sorted);
        Assert.True(RigLitres.Create(2) < RigLitres.Create(5));
        Assert.True(RigLitres.Create(5) >= RigLitres.Create(5));
        Assert.True(null < RigLitres.Create(1));
        Assert.Throws<ArgumentException>(() => ((IComparable)RigLitres.Create(1)).CompareTo(RigAxles.Create(2)));
    }

    // Not async: a TestDatabase binds the flow that starts it, and a binding made inside an async helper ends when the
    // helper returns — so the database is started here, in the test's flow, and only the seeding awaits.
    private Task<TestDatabase> StartWithRigsAsync() => WithRigs(StartDatabaseAsync());

    private static async Task<TestDatabase> WithRigs(Task<TestDatabase> starting)
    {
        var database = await starting;
        var token = TestContext.Current.CancellationToken;
        await Rig.Of("Ada", "ABC-123", 10.5m, 3, fueled: new DateTime(2024, 1, 1)).Save(cancellationToken: token);
        await Rig.Of("Bea", "ABC-124", 20, 3, fueled: new DateTime(2024, 3, 1)).Save(cancellationToken: token);
        await Rig.Of("Cid", "ABC-125", 30, 3, fueled: new DateTime(2024, 2, 1)).Save(cancellationToken: token);
        return database;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}

/// <summary>A wall-clock moment stored as the UTC instant it is, for a database that keeps only UTC.</summary>
public sealed class WallClockInUtcTests
{
    [Fact]
    public void A_wall_clock_time_goes_to_UTC_and_comes_back_as_it_was()
    {
        var noon = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Unspecified);

        var utc = WallClockInUtc.ToUtc(noon);
        var back = WallClockInUtc.ToWall(utc);

        Assert.Equal((TimeZoneInfo.ConvertTimeToUtc(noon, Clock.Zone), DateTimeKind.Utc), (utc, utc.Kind));
        Assert.Equal((noon, DateTimeKind.Unspecified), (back, back.Kind));
    }

    [Fact]
    public void A_wall_time_the_clock_skips_is_stored_as_the_instant_after_the_jump()
    {
        var skipped = Enumerable.Range(0, 366 * 24).Select(h => new DateTime(2026, 1, 1).AddHours(h).AddMinutes(30))
            .Where(Clock.Zone.IsInvalidTime).Take(1).ToList();

        var stored = skipped.Select(WallClockInUtc.ToUtc).ToList();

        Assert.All(stored, s => Assert.Equal(DateTimeKind.Utc, s.Kind));
        Assert.All(stored, s => Assert.False(Clock.Zone.IsInvalidTime(WallClockInUtc.ToWall(s))));
    }
}
