using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
///     A save tells the current session which aggregates it wrote — once it is durable, and only a session.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class DataChangesTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-changes-{Guid.NewGuid():N}.db");
    private readonly Recorder _recorder = new();

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_save_in_a_session_tells_it_which_aggregates_it_wrote()
    {
        await using var database = await StartDatabaseAsync();

        using (Db.UseScope(Session(_recorder)))
        {
            database.Context.Add(RateCard.For("HU"));
            await database.Context.SaveChangesAsync();
        }

        Assert.Equal([typeof(RateCard)], Assert.Single(_recorder.Saves));
    }

    [Fact]
    public async Task A_child_is_reported_as_the_aggregate_that_owns_it()
    {
        await using var database = await StartDatabaseAsync();
        var tenant = Guid.NewGuid();

        using (Tenant.Use(tenant))
        using (Db.UseScope(Session(_recorder)))
        {
            var ledger = Ledger.For("L-1");
            ledger.Add("opening balance");
            database.Context.Add(ledger);
            await database.Context.SaveChangesAsync();
        }

        // A screen asks for the ledger, never for its entries on their own.
        var saved = Assert.Single(_recorder.Saves);
        Assert.Equal([typeof(Ledger)], saved);
    }

    [Fact]
    public async Task Outside_a_session_nobody_is_told()
    {
        await using var database = await StartDatabaseAsync();

        // A background job: no screen is waiting, and another session's cache is not its to touch.
        database.Context.Add(RateCard.For("HU"));
        await database.Context.SaveChangesAsync();

        Assert.Empty(_recorder.Saves);
    }

    [Fact]
    public async Task Inside_a_transaction_it_waits_for_the_commit()
    {
        await using var database = await StartDatabaseAsync();

        using (Db.UseScope(Session(_recorder)))
        {
            await using var transaction = await database.Context.Database.BeginTransactionAsync();
            database.Context.Add(RateCard.For("HU"));
            await database.Context.SaveChangesAsync();

            // Written, not committed: a refetch now could read the rows as they were and cache that.
            Assert.Empty(_recorder.Saves);

            await transaction.CommitAsync();
        }

        Assert.Equal([typeof(RateCard)], Assert.Single(_recorder.Saves));
    }

    [Fact]
    public async Task A_rolled_back_transaction_tells_nobody()
    {
        await using var database = await StartDatabaseAsync();

        using (Db.UseScope(Session(_recorder)))
        {
            await using var transaction = await database.Context.Database.BeginTransactionAsync();
            database.Context.Add(RateCard.For("HU"));
            await database.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        Assert.Empty(_recorder.Saves);
    }

    [Fact]
    public async Task An_observer_that_throws_does_not_fail_a_save_that_committed()
    {
        await using var database = await StartDatabaseAsync();
        var card = RateCard.For("HU");

        using (Db.UseScope(Session(new Throwing())))
        {
            database.Context.Add(card);

            // The row is in: surfacing the observer's fault would make the caller retry a done write.
            await database.Context.SaveChangesAsync();
        }

        database.Context.ChangeTracker.Clear();
        Assert.True(await database.Context.Set<RateCard>().AnyAsync(r => r.Id == card.Id));
    }

    private static IServiceProvider Session(IDataChanges observer) => new SessionScope(observer);

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}").AddInterceptors(new DataChangesInterceptor()));

    private sealed class Recorder : IDataChanges
    {
        public List<Type[]> Saves { get; } = [];

        public void Saved(IReadOnlyCollection<Type> entityTypes) => Saves.Add([.. entityTypes]);
    }

    private sealed class Throwing : IDataChanges
    {
        public void Saved(IReadOnlyCollection<Type> entityTypes) => throw new InvalidOperationException("observer");
    }

    /// <summary>What a session's scope answers: its observers, and nothing else.</summary>
    private sealed class SessionScope(IDataChanges observer) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IEnumerable<IDataChanges>) ? new[] { observer } : null;
    }
}
