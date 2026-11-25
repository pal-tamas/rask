using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>
///     A save of the fixture's own context the database refused with a sentence of the model's — a unique index's
///     <c>IsUnique("…")</c> — leaves nothing behind for the next save to carry, inside a <c>Db.Transaction</c> or not.
/// </summary>
public sealed class FixtureContextRefusalTests
{
    private const string Partner = "Acme";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_save_refused_with_a_unique_sentence_does_not_ride_along_with_the_next()
    {
        var named = Named();
        await using var alive = await Alive(named);
        await using var database = await TestDatabase.Start<NeutralContext>(o => o.UseSqlite(named), cancellationToken: Ct);
        await LiveGrantElsewhere(database);

        database.Context.Add(Grant());
        var refused = await Record.ExceptionAsync(() => database.Context.SaveChangesAsync(Ct));
        database.Context.Add(Grant("Other"));
        await database.Context.SaveChangesAsync(Ct);

        Assert.IsType<RaskValidationException>(refused);
        Assert.Equal([Partner, "Other"], await PartnersInTheDatabase(database));
    }

    [Fact]
    public async Task A_save_refused_with_a_unique_sentence_inside_a_transaction_does_not_ride_along_with_the_next()
    {
        var named = Named();
        await using var alive = await Alive(named);
        await using var database = await TestDatabase.Start<NeutralContext>(o => o.UseSqlite(named), cancellationToken: Ct);
        await LiveGrantElsewhere(database);

        database.Context.Add(Grant());
        var refused = await Record.ExceptionAsync(() => Db.Transaction(() => database.Context.SaveChangesAsync(Ct), cancellationToken: Ct));
        database.Context.Add(Grant("Other"));
        await database.Context.SaveChangesAsync(Ct);

        Assert.IsType<RaskValidationException>(refused);
        Assert.Equal([Partner, "Other"], await PartnersInTheDatabase(database));
    }

    private static string Named() => $"Data Source=refusal-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    // An in-memory database lives while a connection to it is open. The database is started in the test itself, which
    // it binds: a TestDatabase started in an async helper is unbound again once the helper returns.
    private static async Task<SqliteConnection> Alive(string named)
    {
        var alive = new SqliteConnection(named);
        await alive.OpenAsync(Ct);
        return alive;
    }

    private static PartnerGrant Grant(string partner = Partner) => new() { Id = Guid.NewGuid(), Partner = partner, Active = true };

    private static async Task LiveGrantElsewhere(TestDatabase<NeutralContext> database)
    {
        await using var other = database.Factory.CreateDbContext();
        other.Add(Grant());
        await other.SaveChangesAsync(Ct);
    }

    private static async Task<List<string>> PartnersInTheDatabase(TestDatabase<NeutralContext> database)
    {
        await using var fresh = database.Factory.CreateDbContext();
        return await fresh.Set<PartnerGrant>().OrderBy(g => g.Partner).Select(g => g.Partner).ToListAsync(Ct);
    }
}
