using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// F-42 + F-16: a public method of a partial aggregate is run by its generated static entry as one write — loaded at
// the caller's version, the roles its Configure names asked first, the method called, the aggregate saved with its
// rules asked — and nothing of it is written when any part refuses.
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
        var voyage = Voyage.Plan("North", 2);
        await voyage.Save(cancellationToken: Token);

        await Voyage.Book(voyage.Id, 0, 7, Token);

        var stored = await database.Load<Voyage>(voyage.Id, Token);
        Assert.Equal(7, stored!.Seats);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task An_entry_at_a_stale_version_is_refused_before_the_method_runs()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        await voyage.Save(cancellationToken: Token);
        await Voyage.Book(voyage.Id, 0, 7, Token);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Voyage.Book(voyage.Id, 0, 9, Token));

        Assert.Empty(Assert.Single(refused.Failures).Fields);
        Assert.Equal(7, (await database.Load<Voyage>(voyage.Id, Token))!.Seats);
    }

    [Fact]
    public async Task A_change_the_aggregates_rules_refuse_is_written_nowhere()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        await voyage.Save(cancellationToken: Token);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Voyage.Book(voyage.Id, 0, 500, Token));

        Assert.Equal(["Seats"], refused.Errors.Keys);
        var stored = await database.Load<Voyage>(voyage.Id, Token);
        Assert.Equal(2, stored!.Seats);
        Assert.Equal(0, stored.Version);
    }

    [Fact]
    public async Task An_entry_for_an_id_no_row_has_is_KeyNotFound()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Voyage.Book(Guid.NewGuid(), 0, 7, Token));
    }

    [Fact]
    public async Task An_operation_its_Configure_gives_roles_refuses_anyone_without_one_before_anything_is_read()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        voyage.Add("Oslo", 2);
        await voyage.Save(cancellationToken: Token);
        using var signedIn = Current.UsePrincipal(Signed("Deckhand"));

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Voyage.Seal(voyage.Id, 0, Token));

        Assert.True(refused.IsAuthenticated);
        Assert.Contains("Voyage.Seal needs one of the roles 'Captain,Admiral'", refused.Message, StringComparison.Ordinal);
        Assert.False((await database.Load<Voyage>(voyage.Id, Token))!.Sealed);
    }

    [Fact]
    public async Task An_operation_its_Configure_gives_roles_runs_for_anyone_who_holds_one()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        voyage.Add("Oslo", 2);
        await voyage.Save(cancellationToken: Token);
        using var signedIn = Current.UsePrincipal(Signed("Admiral"));

        await Voyage.Seal(voyage.Id, 0, Token);

        Assert.True((await database.Load<Voyage>(voyage.Id, Token))!.Sealed);
    }

    [Fact]
    public async Task Nobody_signed_in_is_refused_an_operation_that_names_roles_as_not_signed_in()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        await voyage.Save(cancellationToken: Token);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Voyage.Seal(voyage.Id, 0, Token));

        Assert.False(refused.IsAuthenticated);
    }

    [Fact]
    public async Task An_entry_inside_a_transaction_that_rolls_back_writes_nothing()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        await voyage.Save(cancellationToken: Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await Voyage.Book(voyage.Id, 0, 7, Token);
            throw new InvalidOperationException("Something after it failed.");
        }, cancellationToken: Token));

        Assert.Equal(2, (await database.Load<Voyage>(voyage.Id, Token))!.Seats);
    }

    private static ClaimsPrincipal Signed(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], "Test"));

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);
}
