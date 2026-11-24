using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// F-47: an operation tried on a copy of the aggregate, its rules asked as a save asks them, and nothing written, locked
// or announced — what a form over the operation asks while it is filled in.
[Collection(DataDbCollection.Name)]
public sealed class DryRunTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-dryrun-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => File.Delete(_dbPath);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_change_its_rules_refuse_is_told_under_its_field_and_written_nowhere()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();

        var failures = await Expedition.DryRunBook(expedition.Id, 0, 500, Token);

        Assert.Equal(["Seats"], Assert.Single(failures).Fields);
        var stored = await database.Load<Expedition>(expedition.Id, Token);
        Assert.Equal(2, stored!.Seats);
        Assert.Equal(0, stored.Version);
    }

    [Fact]
    public async Task A_change_that_would_go_through_is_told_nothing_and_still_written_nowhere()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();

        var failures = await Expedition.DryRunBook(expedition.Id, 0, 7, Token);

        Assert.Empty(failures);
        var stored = await database.Load<Expedition>(expedition.Id, Token);
        Assert.Equal(2, stored!.Seats);
        Assert.Equal(0, stored.Version);
    }

    [Fact]
    public async Task A_refusal_inside_the_method_is_told_as_the_operation_would_be_refused()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();
        var stored = (await Expedition.Find(expedition.Id, Token))!;
        stored.Seal();
        await stored.Save(cancellationToken: Token);

        var failures = await Expedition.DryRunRename(expedition.Id, 1, "South", Token);

        var failure = Assert.Single(failures);
        Assert.Equal("A sealed expedition keeps its name.", failure.Message);
        Assert.Equal(["Title"], failure.Fields);
    }

    [Fact]
    public async Task A_version_saved_over_since_is_told_about_the_whole_form()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();
        await Expedition.Book(expedition.Id, 0, 7, Token);

        var failures = await Expedition.DryRunBook(expedition.Id, 0, 9, Token);

        Assert.Empty(Assert.Single(failures).Fields);
    }

    [Fact]
    public async Task A_lock_the_rules_take_is_not_taken_by_a_dry_run()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();
        await using var held = await Db.Lock(Expedition.Lock, Token);

        var failures = await Expedition.DryRunBook(expedition.Id, 0, 7, Token);

        Assert.Empty(failures);
        await Assert.ThrowsAsync<TimeoutException>(() => Expedition.Book(expedition.Id, 0, 7, Token));
    }

    [Fact]
    public async Task A_factory_tried_tells_what_its_rules_refuse_and_inserts_nothing()
    {
        await using var database = await StartDatabaseAsync();

        var failures = await Expedition.DryRunPlan("Far too long a name", 2, Token);

        Assert.Equal(["Title"], Assert.Single(failures).Fields);
        Assert.Empty(await database.Context.Set<Expedition>().ToListAsync(Token));
    }

    private static async Task<Expedition> PlannedAsync()
    {
        var expedition = Expedition.Planned("North", 2);
        await expedition.Save(cancellationToken: Token);
        return expedition;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);
}
