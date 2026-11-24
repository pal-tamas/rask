using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

// Test classes are xUnit collections of their own, so these run in parallel — each with a database it started itself, and
// each reading back only its own rows through every way an app reads: the read face, the aggregate's statics, a handler,
// and a context factory a test host was handed.

public sealed class SideBySideOneTests
{
    [Fact]
    public Task A_test_beside_others_reads_only_its_own_rows_every_way() => SideBySide.Seed(companies: 1);
}

public sealed class SideBySideTwoTests
{
    [Fact]
    public Task A_test_beside_others_reads_only_its_own_rows_every_way() => SideBySide.Seed(companies: 2);
}

public sealed class SideBySideThreeTests
{
    [Fact]
    public Task A_test_beside_others_reads_only_its_own_rows_every_way() => SideBySide.Seed(companies: 3);
}

internal static class SideBySide
{
    // Holds every test until all three have written, so their databases are open at once; bounded, so a runner that does
    // not run them together still finishes — the checks hold either way.
    private static readonly CountdownEvent AllWritten = new(3);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task Seed(int companies)
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        for (var i = 0; i < companies; i++)
        {
            database.Context.Add(Company.Named($"Company {i}"));
        }

        database.Context.Add(Ledger.Open($"Ledger of {companies}"));
        await database.Context.SaveChangesAsync(Ct);
        AllWritten.Signal();
        await Task.Run(() => AllWritten.Wait(TimeSpan.FromSeconds(10), Ct), Ct);

        var readFace = await Company.Where(c => c.Name.StartsWith("Company")).Count(Ct);
        var statics = await Company.Count(Ct);
        var lines = await LedgerLines(database);
        var throughHost = await ThroughATestHost(database);

        Assert.Equal(companies, readFace);
        Assert.Equal(companies, statics);
        Assert.Equal([$"Opened Ledger of {companies}"], lines);
        Assert.Equal(companies, throughHost);
    }

    private static async Task<List<string>> LedgerLines(TestDatabase<PumpsContext> database)
    {
        await using var db = await database.Factory.CreateDbContextAsync(Ct);
        return await db.Set<LedgerLine>().Select(l => l.Text).ToListAsync(Ct);
    }

    // A service under test that takes the app's context factory, built by the test in a container of its own.
    private static async Task<int> ThroughATestHost(TestDatabase<PumpsContext> database)
    {
        await using var host = new ServiceCollection().AddSingleton(database.Factory).BuildServiceProvider();
        await using var db = await host.GetRequiredService<IDbContextFactory<PumpsContext>>().CreateDbContextAsync(Ct);
        return await db.Set<Company>().CountAsync(Ct);
    }
}
