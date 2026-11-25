using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data.Tests;

// F-18 on the generated entries: `b.Operation(x => x.M, manyAggregates: "…")` runs it in a Db.Transaction with the
// reason; `forUpdate: true` / `forUpdate: x => x.Wagons` and `id.Load(version, forUpdate: true)` read the rows under
// the update lock first — on SQLite the read is written as is (its one writer serializes), so the tag shows it ran.
public sealed class OperationLockTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();
    private readonly ReadRecorder _reads = new();

    public void Dispose() => _sqlite.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_operation_whose_configure_states_many_aggregates_writes_a_second_one()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync();

        await Caravan.Supply(caravan.Id, 0, "anvil", Token);

        Assert.Equal(1, await Widget.Count(Token));
        Assert.Equal("supplied", (await database.Load<Caravan>(caravan.Id, Token))!.Leader);
    }

    [Fact]
    public async Task An_operation_that_states_no_reason_is_refused_its_second_aggregate()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync();

        await Assert.ThrowsAsync<ManyAggregatesException>(() => Caravan.Stock(caravan.Id, 0, "anvil", Token));

        Assert.Equal(0, await Widget.Count(Token));
        Assert.Equal("Ann", (await database.Load<Caravan>(caravan.Id, Token))!.Leader);
    }

    [Fact]
    public async Task For_update_true_reads_the_aggregates_row_under_the_lock_before_the_load()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync();
        _reads.Locked.Clear();

        await Caravan.Lead(caravan.Id, 0, "Bea", Token);

        Assert.Contains("\"Caravan\"", Assert.Single(_reads.Locked), StringComparison.Ordinal);
        Assert.Equal("Bea", (await database.Load<Caravan>(caravan.Id, Token))!.Leader);
    }

    [Fact]
    public async Task For_update_on_a_child_collection_reads_its_rows_under_the_lock()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync();
        _reads.Locked.Clear();

        await Caravan.Hitch(caravan.Id, 0, "salt", Token);

        Assert.Contains("\"CaravanWagon\"", Assert.Single(_reads.Locked), StringComparison.Ordinal);
        Assert.Equal("salt", Assert.Single((await GeneratedModelWrites.Find<Caravan>(caravan.Id, Token))!.Wagons).Load);
    }

    [Fact]
    public async Task An_id_loads_its_aggregate_under_the_lock_inside_a_transaction()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync();
        _reads.Locked.Clear();
        Caravan? loaded = null;

        await Db.Transaction(async () => loaded = await caravan.Id.Load(0, forUpdate: true, Token), cancellationToken: Token);

        Assert.Equal("Ann", loaded!.Leader);
        Assert.Single(_reads.Locked);
    }

    [Fact]
    public async Task An_id_locked_outside_a_transaction_is_refused_and_one_not_locked_loads()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => caravan.Id.Load(forUpdate: true, cancellationToken: Token));

        Assert.Contains("Db.Transaction", refused.Message, StringComparison.Ordinal);
        Assert.Equal("Ann", (await caravan.Id.Load(cancellationToken: Token)).Leader);
    }

    [Fact]
    public async Task An_id_no_row_has_is_refused_as_missing()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => new CaravanId(Guid.CreateVersion7()).Load(cancellationToken: Token));
    }

    private static async Task<Caravan> FormedAsync()
    {
        var caravan = Caravan.Formed("Ann");
        await caravan.Save(cancellationToken: Token);
        return caravan;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString).AddInterceptors(_reads), cancellationToken: Token);

    // The reads tagged for the update lock, as the database was sent them.
    private sealed class ReadRecorder : DbCommandInterceptor
    {
        public List<string> Locked { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("rask:for-update", StringComparison.Ordinal))
            {
                Locked.Add(command.CommandText);
            }

            return ValueTask.FromResult(result);
        }
    }
}
