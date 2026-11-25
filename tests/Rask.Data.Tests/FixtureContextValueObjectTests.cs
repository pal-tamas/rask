using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
///     The fixture's own context reads a row another context saved as saved, value objects of several values included —
///     the columns EF Core keeps as a complex property.
/// </summary>
public sealed class FixtureContextValueObjectTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_value_object_another_context_saved_reads_as_saved_through_the_fixture_context()
    {
        var named = $"Data Source=fixture-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var alive = new SqliteConnection(named); // an in-memory database lives while a connection is open
        await alive.OpenAsync(Ct);
        await using var database = await TestDatabase.Start(o => o.UseSqlite(named), cancellationToken: Ct);
        var harbor = Harbor.Full("Szeged");
        database.Context.Add(harbor);
        await database.Context.SaveChangesAsync(Ct);
        var model = (await Harbor.Model(harbor.Id, cancellationToken: Ct))!;
        model.Address!.Floor = "5";

        await Harbor.Update(harbor.Id, model, cancellationToken: Ct);
        var read = await database.Context.Set<Harbor>().SingleAsync(h => h.Id == harbor.Id, Ct);

        Assert.Equal("5", read.Address.Floor);
        Assert.Equal(EntityState.Unchanged, database.Context.Entry(read).State);
    }
}
