using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

// TestDatabase.Start<TContext>() with nothing to configure: a SQLite database in memory, of the test's own. Not in the
// TestDatabase collection on purpose — these run beside each other and beside it.
public sealed class InMemoryDatabaseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_context_the_database_hands_out_sees_the_same_rows()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var company = Company.Named("Acme");
        database.Context.Add(company);
        await database.Context.SaveChangesAsync(Ct);

        await using var fromFactory = await database.Factory.CreateDbContextAsync(Ct);
        var factoryCount = await fromFactory.Set<Company>().CountAsync(Ct);
        var surfaceCount = await Company.Count(Ct);
        var loaded = await database.Load<Company>(company.Id, Ct);

        Assert.Equal(1, factoryCount);
        Assert.Equal(1, surfaceCount);
        Assert.Equal("Acme", loaded!.Name);
    }

    [Fact]
    public async Task Two_databases_started_side_by_side_each_see_only_their_own_rows()
    {
        using var bothStarted = new Barrier(2);

        var counts = await Task.WhenAll(Seeded(1), Seeded(3));

        Assert.Equal([1, 3], counts);

        async Task<int> Seeded(int companies)
        {
            await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
            for (var i = 0; i < companies; i++)
            {
                database.Context.Add(Company.Named($"Company {i}"));
            }

            await database.Context.SaveChangesAsync(Ct);
            await Task.Run(() => bothStarted.SignalAndWait(Ct), Ct);
            return await Company.Count(Ct);
        }
    }

    [Fact]
    public async Task Disposing_the_database_lets_it_go()
    {
        string connectionString;
        await using (var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct))
        {
            connectionString = database.Context.Database.GetConnectionString()!;
        }

        await using var reopened = new SqliteConnection(connectionString);
        await reopened.OpenAsync(Ct);
        await using var tables = reopened.CreateCommand();
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'";
        var left = (long)(await tables.ExecuteScalarAsync(Ct))!;

        Assert.Equal(0, left);
        Assert.Contains("Mode=Memory", connectionString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decimals_order_as_numbers()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        database.Context.AddRange(Invoice.For("a", 19.95m), Invoice.For("b", 2.50m), Invoice.For("c", 100.00m));
        await database.Context.SaveChangesAsync(Ct);

        await using var db = await database.Factory.CreateDbContextAsync(Ct);
        var totals = await db.Set<Invoice>().OrderBy(i => i.Total).Select(i => i.Total).ToListAsync(Ct);

        Assert.Equal([2.50m, 19.95m, 100.00m], totals);
    }

    [Fact]
    public async Task Text_orders_and_compares_as_the_app_s_SQL_Server_collation_does()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        database.Context.AddRange(Invoice.For("Csaba", 1), Invoice.For("Cecil", 2), Invoice.For("Ödön", 3), Invoice.For("Ottó", 4));
        await database.Context.SaveChangesAsync(Ct);

        await using var db = await database.Factory.CreateDbContextAsync(Ct);
        var ordered = await db.Set<Invoice>().OrderBy(i => i.Customer.InTextOrder()).Select(i => i.Customer).ToListAsync(Ct);
        var found = await db.Set<Invoice>().CountAsync(i => i.Customer == "CSABA" || i.Customer.Contains("ödö"), Ct);

        Assert.Equal(["Cecil", "Csaba", "Ottó", "Ödön"], ordered);
        Assert.Equal(2, found);
    }

    [Fact]
    public async Task The_read_face_orders_the_whole_Hungarian_alphabet_as_the_app_s_collation_does()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        string[] customers = ["Úr", "Csaba", "Ödön", "Cukor", "Uzsok", "Ottó", "Ügy", "Őz", "Zsolt", "Zoltán"];
        database.Context.AddRange(customers.Select(static c => Invoice.For(c, 1)));
        await database.Context.SaveChangesAsync(Ct);

        var ordered = await Invoice.OrderBy(i => i.Customer.InTextOrder()).Select(i => i.Customer);

        Assert.Equal(["Cukor", "Csaba", "Ottó", "Ödön", "Őz", "Úr", "Uzsok", "Ügy", "Zoltán", "Zsolt"], ordered);
    }

    [Fact]
    public async Task A_database_over_Rask_s_own_context_starts_in_memory_too()
    {
        await using var database = await TestDatabase.Start(cancellationToken: Ct);
        database.Context.Add(Company.Named("Acme"));
        await database.Context.SaveChangesAsync(Ct);

        var companies = await Company.Count(Ct);

        Assert.Equal(1, companies);
    }
}
