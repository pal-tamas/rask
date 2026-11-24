using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// `rask audit --unique-candidates`: every unique index the model declares, asked of the stored rows as if the database
// did not have it yet — each group of rows that would collide, by their ids. The indexes are dropped here, as a database
// is before the migration that adds them.
[Collection(DataDbCollection.Name)]
public sealed class UniqueCandidateTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rows_that_hold_the_same_key_are_reported_as_one_group_by_their_ids()
    {
        await using var database = await StartDatabaseAsync();
        await DropIndexesAsync(database);
        var first = Keycard.Issue("B-1");
        var second = Keycard.Issue("B-1");
        await first.Save(cancellationToken: Token);
        await second.Save(cancellationToken: Token);
        await Keycard.Issue("B-2").Save(cancellationToken: Token);

        var report = await RuleAudit.RunAsync(["Keycard"], Token, uniqueCandidates: true);

        var breach = Assert.Single(report.Breaches);
        Assert.Equal(("Keycard", "Number"), (breach.Aggregate, breach.Field));
        Assert.Equal([$"{first.Id} = {second.Id}"], breach.Ids);
        Assert.Equal([new RuleAuditCount("Keycard", 3)], report.Read);
    }

    [Fact]
    public async Task Texts_that_differ_only_in_letter_case_are_reported_apart_as_a_case_insensitive_collision()
    {
        await using var database = await StartDatabaseAsync();
        await DropIndexesAsync(database);
        await Keycard.Issue("b-1").Save(cancellationToken: Token);
        await Keycard.Issue("B-1").Save(cancellationToken: Token);

        var report = await RuleAudit.RunAsync(["Keycard"], Token, uniqueCandidates: true);

        Assert.Contains("case-insensitive", Assert.Single(report.Breaches).Rule, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_null_key_collides_with_nothing_and_a_filtered_index_asks_only_the_rows_it_keeps()
    {
        await using var database = await StartDatabaseAsync();
        await DropIndexesAsync(database);
        await Keycard.Issue("B-1").Save(cancellationToken: Token);
        await Keycard.Issue("B-2").Save(cancellationToken: Token);
        await Keycard.Issue("B-3", "Anna", revoked: true).Save(cancellationToken: Token);
        await Keycard.Issue("B-4", "Anna", revoked: true).Save(cancellationToken: Token);
        var held = Keycard.Issue("B-5", "Bela");
        var heldAgain = Keycard.Issue("B-6", "Bela");
        await held.Save(cancellationToken: Token);
        await heldAgain.Save(cancellationToken: Token);

        var report = await RuleAudit.RunAsync(["Keycard"], Token, uniqueCandidates: true);

        var breach = Assert.Single(report.Breaches);
        Assert.Equal(("Holder", $"{held.Id} = {heldAgain.Id}"), (breach.Field, Assert.Single(breach.Ids)));
    }

    [Fact]
    public async Task Rows_an_index_the_database_holds_already_kept_unique_answer_clean()
    {
        await using var database = await StartDatabaseAsync();
        await Keycard.Issue("B-1", "Anna").Save(cancellationToken: Token);
        await Keycard.Issue("B-2", "Bela").Save(cancellationToken: Token);

        var report = await RuleAudit.RunAsync(["Keycard"], Token, uniqueCandidates: true);

        Assert.True(report.Clean);
        Assert.Empty(report.Notes);
    }

    // The database as it is before the migration that adds the indexes.
    private static async Task DropIndexesAsync(TestDatabase database)
    {
        foreach (var index in database.Context.Model.FindEntityType(typeof(Keycard))!.GetIndexes())
        {
#pragma warning disable EF1002 // the name is the model's own
            await database.Context.Database.ExecuteSqlRawAsync($"DROP INDEX \"{index.GetDatabaseName()}\"", Token);
#pragma warning restore EF1002
        }
    }

    // A named in-memory database, kept alive by the connection the database holds. Not async: see RuleAuditTests.
    private static Task<TestDatabase> StartDatabaseAsync()
    {
        var keeper = new SqliteConnection($"Data Source=unique-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        keeper.Open();
        return TestDatabase.StartHolding(o => o.UseSqlite(keeper.ConnectionString), keeper, null, null, Token);
    }
}
