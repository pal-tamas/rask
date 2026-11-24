using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

// Test.Concurrently over a database of the test's own: two saves of the version one form was filled from, side by side
// on SQLite in memory — one is kept, the other refused as stale, and neither waits on the other forever.
public sealed class ConcurrentlyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_saves_of_one_version_side_by_side_keep_one_and_refuse_the_other()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var company = Company.Named("Acme");
        database.Context.Add(company);
        await database.Context.SaveChangesAsync(Ct);
        var version = company.Version;

        var refusal = await Test.Concurrently(
            () => Company.Update(company.Id, c => c.Rename("Acme Ltd"), version: version),
            () => Company.Update(company.Id, c => c.Rename("Acme Inc"), version: version));

        refusal.Whole();
        var kept = await database.Load<Company>(company.Id, Ct);
        Assert.Contains(kept!.Name, new[] { "Acme Ltd", "Acme Inc" });
        Assert.NotEqual(version, kept.Version);
    }
}
