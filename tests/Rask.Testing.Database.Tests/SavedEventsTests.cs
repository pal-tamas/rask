using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

// A TestDatabase saves through the interceptors an app's context has, domain-event publication among them.
public sealed class SavedEventsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_saved_aggregate_s_event_reaches_its_handler_which_writes_into_the_same_database()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var ledger = Ledger.Open("Fuel");

        database.Context.Add(ledger);
        await database.Context.SaveChangesAsync(Ct);

        var lines = await LinesAsync(database);
        Assert.Equal(["Opened Fuel"], lines);
        Assert.Empty(ledger.DomainEvents);
    }

    [Fact]
    public async Task A_save_that_fails_publishes_nothing_and_keeps_its_events()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var refused = Ledger.Open("Fuel");
        database.Context.Add(refused);
        database.Context.Entry(refused).Property(l => l.Name).CurrentValue = null!; // the column is NOT NULL

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => database.Context.SaveChangesAsync(Ct));

        Assert.Empty(await LinesAsync(database));
        Assert.Single(refused.DomainEvents);
    }

    [Fact]
    public async Task The_test_s_own_services_reach_the_handlers()
    {
        var renames = new Renames();
        await using var database = await TestDatabase.Start<PumpsContext>(services: s => s.AddSingleton(renames), cancellationToken: Ct);
        var ledger = Ledger.Open("Fuel");
        database.Context.Add(ledger);
        await database.Context.SaveChangesAsync(Ct);

        ledger.Rename("Diesel");
        await database.Context.SaveChangesAsync(Ct);

        Assert.Equal(["Diesel"], renames.Heard);
    }

    private static async Task<List<string>> LinesAsync(TestDatabase<PumpsContext> database)
    {
        await using var db = await database.Factory.CreateDbContextAsync(Ct);
        return await db.Set<LedgerLine>().Select(l => l.Text).ToListAsync(Ct);
    }
}
