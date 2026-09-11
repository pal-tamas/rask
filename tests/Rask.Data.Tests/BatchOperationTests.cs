using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// Set-based update and delete: one statement, no entities loaded. These pin both halves — that they
// work, and what they deliberately skip.
[Collection(DataDbCollection.Name)]
public sealed class BatchOperationTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-batch-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task ExecuteUpdate_writes_every_matching_row_in_one_statement()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "a", "b", "c");

        var updated = await Order.Where(o => o.Reference != "b")
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Shipped));

        Assert.Equal(2, updated);
        Assert.Equal(2, await Order.CountAsync(o => o.Status == OrderStatus.Shipped));
        Assert.Equal(1, await Order.CountAsync(o => o.Status == OrderStatus.Open));
    }

    [Fact]
    public async Task A_setter_can_read_the_row_it_is_updating()
    {
        // The reason to reach for this rather than load-and-save: the arithmetic happens in the database.
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "a", "b");

        await Order.All.ExecuteUpdateAsync(s => s.SetProperty(o => o.Reference, o => o.Reference + "-x"));

        var references = await Order.OrderBy(o => o.Reference).Select(o => o.Reference).ToListAsync();
        Assert.Equal(["a-x", "b-x"], references);
    }

    [Fact]
    public async Task ExecuteUpdate_does_not_stamp_UpdatedAt_and_the_caller_has_to()
    {
        // Documented, not accidental: nothing was loaded, so no interceptor ran. This test exists so the
        // day that changes, it changes here first rather than in somebody's audit trail.
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "a");

        var before = (await Order.FirstOrDefaultAsync(o => o.Reference == "a"))!.UpdatedAt;

        await Order.All.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Shipped));
        Assert.Equal(before, (await Order.FirstOrDefaultAsync(o => o.Reference == "a"))!.UpdatedAt);

        // Setting it explicitly is the supported way round.
        await Order.All.ExecuteUpdateAsync(s => s.SetProperty(o => o.UpdatedAt, Now));
        Assert.Equal(Now, (await Order.FirstOrDefaultAsync(o => o.Reference == "a"))!.UpdatedAt);
    }

    [Fact]
    public async Task A_batch_soft_delete_is_an_update_of_DeletedAt()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "a", "b", "c");

        var stamped = await Order.Where(o => o.Reference != "c")
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.DeletedAt, Now));

        Assert.Equal(2, stamped);

        // The global query filter hides them, exactly as a tracked soft delete would.
        Assert.Equal(1, await Order.CountAsync());
        Assert.Equal(3, await Order.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ExecuteDelete_really_deletes_a_soft_deletable_row()
    {
        // The trap worth a test: Remove() on an ISoftDeletable stamps DeletedAt, but ExecuteDelete is a
        // DELETE statement and the interceptors never see it. The rows are gone, not hidden.
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, "a", "b");

        var deleted = await Order.Where(o => o.Reference == "a").ExecuteDeleteAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(1, await Order.IgnoreQueryFilters().CountAsync());
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));

    private static async Task SeedAsync(TestDatabase database, params string[] references)
    {
        database.Context.AddRange(references.Select(Order.Place));
        await database.Context.SaveChangesAsync();
    }
}
