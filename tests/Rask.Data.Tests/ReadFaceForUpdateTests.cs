using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data.Tests;

// F-19: a read face's own ForUpdate() step reads its rows under the update lock inside a Db.Transaction — on SQLite the
// read is written as is (its one writer serializes), so the tag shows it ran — and refuses to run outside one.
[Collection(DataDbCollection.Name)]
public sealed class ReadFaceForUpdateTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-read-for-update-{Guid.NewGuid():N}.db");
    private readonly ReadRecorder _reads = new();

    public void Dispose() => File.Delete(_dbPath);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_read_for_update_inside_a_transaction_hands_back_the_rows_read_under_the_lock()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync("Ann");
        _reads.Locked.Clear();
        List<CaravanRead> read = [];

        await Db.Transaction(async () => read = await Caravan.Where(c => c.Id == caravan.Id).ForUpdate(), cancellationToken: Token);

        Assert.Equal("Ann", Assert.Single(read).Leader);
        Assert.Contains("\"Caravan\"", Assert.Single(_reads.Locked), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_projection_and_a_first_of_a_locked_query_read_under_the_lock_too()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync("Bea");
        _reads.Locked.Clear();
        string? leader = null;
        CaravanRead? first = null;

        await Db.Transaction(async () =>
        {
            leader = await Caravan.Where(c => c.Id == caravan.Id).ForUpdate().Select(c => c.Leader).First();
            first = await Caravan.Where(c => c.Id == caravan.Id).ForUpdate().FirstOrDefault();
        }, cancellationToken: Token);

        Assert.Equal("Bea", leader);
        Assert.Equal("Bea", first!.Leader);
        Assert.Equal(2, _reads.Locked.Count);
    }

    [Fact]
    public async Task A_read_for_update_outside_a_transaction_is_refused_naming_the_call_that_holds()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync("Cy");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Caravan.Where(c => c.Id == caravan.Id).ForUpdate());

        Assert.Contains("Caravan.Where(…).ForUpdate()", refused.Message, StringComparison.Ordinal);
        Assert.Contains("await Db.Transaction(async () =>", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_count_or_any_of_a_locked_query_is_refused_on_every_database()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync("Di");
        Exception? counted = null;
        Exception? any = null;
        Exception? projected = null;

        await Db.Transaction(async () =>
        {
            counted = await Record.ExceptionAsync(() => Caravan.Where(c => c.Id == caravan.Id).ForUpdate().Count(Token));
            any = await Record.ExceptionAsync(() => Caravan.Where(c => c.Id == caravan.Id).ForUpdate().Any(Token));
            projected = await Record.ExceptionAsync(() => Caravan.Where(c => c.Id == caravan.Id).ForUpdate().Select(c => c.Leader).Count(Token));
        }, cancellationToken: Token);

        Assert.Contains("Caravan.Where(…).ForUpdate().Count()", Assert.IsType<InvalidOperationException>(counted).Message, StringComparison.Ordinal);
        Assert.Contains("ForUpdate().Any()", Assert.IsType<InvalidOperationException>(any).Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(projected);
    }

    [Fact]
    public async Task A_query_without_the_step_is_read_unlocked_anywhere()
    {
        await using var database = await StartDatabaseAsync();
        var caravan = await FormedAsync("Eve");
        _reads.Locked.Clear();

        var count = await Caravan.Where(c => c.Id == caravan.Id).Count(Token);

        Assert.Equal(1, count);
        Assert.Empty(_reads.Locked);
    }

    private static async Task<Caravan> FormedAsync(string leader)
    {
        var caravan = Caravan.Formed(leader);
        await caravan.Save(cancellationToken: Token);
        return caravan;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}").AddInterceptors(_reads), cancellationToken: Token);

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
