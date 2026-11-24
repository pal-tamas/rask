using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Rask.Values;

namespace Rask.Data.Tests;

// F-42 + F-16: a public method of a partial aggregate is run by its generated static entry as one write — loaded at
// the caller's version, the roles its Configure names asked first, the method called, the aggregate saved with its
// rules asked — and nothing of it is written when any part refuses. A non-public factory is born the same way.
[Collection(DataDbCollection.Name)]
public sealed class OperationEntryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-operations-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => File.Delete(_dbPath);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_entry_loads_the_aggregate_runs_its_method_and_saves_it()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();

        await Expedition.Book(expedition.Id, 0, 7, Token);

        var stored = await database.Load<Expedition>(expedition.Id, Token);
        Assert.Equal(7, stored!.Seats);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task An_awaited_operation_is_awaited_and_its_refusal_lands_under_its_field()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();
        await Expedition.Rename(expedition.Id, 0, "South", Token);
        using (Current.UsePrincipal(Signed("Captain")))
        {
            await Expedition.Seal(expedition.Id, 1, Token);
        }

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Expedition.Rename(expedition.Id, 2, "East", Token));

        Assert.Equal(["Title"], refused.Errors.Keys);
        Assert.Equal("South", (await database.Load<Expedition>(expedition.Id, Token))!.Title);
    }

    [Fact]
    public async Task An_entry_without_a_version_changes_the_aggregate_as_it_now_stands()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();
        await Expedition.Book(expedition.Id, 0, 7, Token);

        await Expedition.Rename(expedition.Id, "North", Token);

        var stored = await database.Load<Expedition>(expedition.Id, Token);
        Assert.Equal(("North", 7), (stored!.Title, stored.Seats));
    }

    [Fact]
    public async Task An_entry_at_a_stale_version_is_refused_before_the_method_runs()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();
        await Expedition.Book(expedition.Id, 0, 7, Token);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Expedition.Book(expedition.Id, 0, 9, Token));

        Assert.Empty(Assert.Single(refused.Failures).Fields);
        Assert.Equal(7, (await database.Load<Expedition>(expedition.Id, Token))!.Seats);
    }

    [Fact]
    public async Task A_change_the_aggregates_rules_refuse_is_written_nowhere()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Expedition.Book(expedition.Id, 0, 500, Token));

        Assert.Equal(["Seats"], refused.Errors.Keys);
        var stored = await database.Load<Expedition>(expedition.Id, Token);
        Assert.Equal(2, stored!.Seats);
        Assert.Equal(0, stored.Version);
    }

    [Fact]
    public async Task An_entry_for_an_id_no_row_has_is_KeyNotFound()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Expedition.Book(Guid.NewGuid(), 0, 7, Token));
    }

    [Fact]
    public async Task An_operation_its_Configure_gives_roles_refuses_anyone_without_one_before_anything_is_read()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();
        using var signedIn = Current.UsePrincipal(Signed("Deckhand"));

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Expedition.Seal(expedition.Id, 0, Token));

        Assert.True(refused.IsAuthenticated);
        Assert.Contains("Expedition.Seal needs one of the roles 'Captain,Admiral'", refused.Message, StringComparison.Ordinal);
        Assert.False(Expedition.MaySeal);
        Assert.False((await database.Load<Expedition>(expedition.Id, Token))!.Sealed);
    }

    [Fact]
    public async Task An_operation_its_Configure_gives_roles_runs_for_anyone_who_holds_one()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();
        using var signedIn = Current.UsePrincipal(Signed("Admiral"));

        await Expedition.Seal(expedition.Id, 0, Token);

        Assert.True(Expedition.MaySeal);
        Assert.True((await database.Load<Expedition>(expedition.Id, Token))!.Sealed);
    }

    [Fact]
    public async Task Nobody_signed_in_is_refused_an_operation_that_names_roles_as_not_signed_in()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Expedition.Seal(expedition.Id, 0, Token));

        Assert.False(refused.IsAuthenticated);
        Assert.False(Expedition.MaySeal);
    }

    [Fact]
    public void An_operation_that_names_no_roles_may_be_run_by_anyone()
    {
        var anyone = Expedition.MayBook;

        Assert.True(anyone);
    }

    [Fact]
    public async Task An_entry_inside_a_transaction_that_rolls_back_writes_nothing()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await Expedition.Book(expedition.Id, 0, 7, Token);
            throw new InvalidOperationException("Something after it failed.");
        }, cancellationToken: Token));

        Assert.Equal(2, (await database.Load<Expedition>(expedition.Id, Token))!.Seats);
    }

    [Fact]
    public async Task A_factorys_entry_builds_saves_and_hands_back_the_id()
    {
        await using var database = await StartDatabaseAsync();
        using var signedIn = Current.UsePrincipal(Signed("Admiral"));

        var id = await Expedition.Plan("North", 2, Token);

        var stored = await database.Load<Expedition>(id, Token);
        Assert.Equal("North", stored!.Title);
        Assert.True(Expedition.MayPlan);
    }

    [Fact]
    public async Task A_factorys_entry_asks_its_roles_and_its_rules_and_saves_nothing_refused()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Expedition.Plan("North", 2, Token));
        using var signedIn = Current.UsePrincipal(Signed("Admiral"));
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Expedition.Plan("Far too long a name", 2, Token));

        Assert.Equal(["Title"], refused.Errors.Keys);
        Assert.Empty(await database.Context.Set<Expedition>().ToListAsync(Token));
    }

    [Fact]
    public async Task The_delete_entry_asks_its_roles_and_deletes_at_the_version_read()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Expedition.Delete(expedition.Id, 0, Token));
        using var signedIn = Current.UsePrincipal(Signed("Admiral"));
        await Expedition.Delete(expedition.Id, 0, Token);

        Assert.True(Expedition.MayDelete);
        Assert.Null(await database.Load<Expedition>(expedition.Id, Token));
    }

    [Fact]
    public async Task The_delete_entry_is_refused_by_the_records_rules_and_a_stale_version()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();
        using var signedIn = Current.UsePrincipal(Signed("Admiral"));
        await Expedition.Seal(expedition.Id, 0, Token);

        var stale = await Assert.ThrowsAsync<RaskValidationException>(() => Expedition.Delete(expedition.Id, 0, Token));
        var kept = await Assert.ThrowsAsync<RaskValidationException>(() => Expedition.Delete(expedition.Id, 1, Token));

        Assert.Empty(Assert.Single(stale.Failures).Fields);
        Assert.Equal("A sealed expedition is kept.", Assert.Single(kept.Failures).Message);
        Assert.NotNull(await database.Load<Expedition>(expedition.Id, Token));
    }

    [Fact]
    public async Task A_change_parameter_keeps_clears_or_sets_what_it_is_about()
    {
        await using var database = await StartDatabaseAsync();
        var expedition = await PlannedAsync();

        await Expedition.Annotate(expedition.Id, 0, Change<string?>.Set("Bring rope"), Token);
        await Expedition.Annotate(expedition.Id, 1, Change<string?>.Keep, Token);
        var kept = (await database.Load<Expedition>(expedition.Id, Token))!.Remark;
        await Expedition.Annotate(expedition.Id, 1, Change<string?>.Clear, Token);   // a keep writes nothing, so no new version

        Assert.Equal("Bring rope", kept);
        Assert.Null((await database.Load<Expedition>(expedition.Id, Token))!.Remark);
    }

    private static async Task<Expedition> PlannedAsync()
    {
        var expedition = Expedition.Planned("North", 2);
        await expedition.Save(cancellationToken: Token);
        return expedition;
    }

    private static ClaimsPrincipal Signed(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], "Test"));

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);
}
