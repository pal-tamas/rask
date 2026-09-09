using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// The point of these: Db.Begin() should be needed only when you want several models in one transaction.
// A single insert, update or delete is a one-liner with no scope in sight.
[Collection(DataDbCollection.Name)]
public sealed class SelfSavingModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-selfsave-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Saving_a_new_model_inserts_it_with_no_unit_of_work()
    {
        await using var database = await StartDatabaseAsync();

        var order = Order.Place("A-1");
        await order.SaveAsync();

        Assert.Equal(1, await Order.CountAsync());
        Assert.Equal("A-1", (await Order.FindAsync(order.Id))!.Reference);
    }

    [Fact]
    public async Task Saving_a_model_that_was_already_persisted_updates_it()
    {
        await using var database = await StartDatabaseAsync();

        var order = Order.Place("A-2");
        await order.SaveAsync();       // insert

        order.Cancel(Now);
        await order.SaveAsync();       // update — not a second row

        Assert.Equal(1, await Order.CountAsync());
        Assert.Equal(OrderStatus.Cancelled, (await Order.FindAsync(order.Id))!.Status);
    }

    [Fact]
    public async Task A_model_read_back_untracked_updates_rather_than_inserting()
    {
        // The round trip that would break a "is the key set?" heuristic: the key is set on a brand-new
        // model too, so CreatedAt is what tells these apart.
        await using var database = await StartDatabaseAsync();

        var order = Order.Place("A-3");
        await order.SaveAsync();

        var loaded = await Order.FirstOrDefaultAsync(o => o.Reference == "A-3");
        loaded!.Cancel(Now);
        await loaded.SaveAsync();

        Assert.Equal(1, await Order.CountAsync());
        Assert.Equal(OrderStatus.Cancelled, (await Order.FindAsync(order.Id))!.Status);
    }

    [Fact]
    public async Task A_model_without_audit_stamps_still_inserts_then_updates()
    {
        // Doodad is a Model with no ITimestamped, so there is no CreatedAt to answer with and SaveAsync
        // has to ask the database. Both directions have to come out right.
        await using var database = await StartDatabaseAsync();

        var doodad = Doodad.Create("first");
        await doodad.SaveAsync();
        Assert.Equal(1, await Doodad.CountAsync());

        await doodad.SaveAsync();       // same row again — an update, not a duplicate
        Assert.Equal(1, await Doodad.CountAsync());

        var loaded = await Doodad.FirstOrDefaultAsync(d => d.Label == "first");
        await loaded!.SaveAsync();      // read back untracked, saved again
        Assert.Equal(1, await Doodad.CountAsync());
    }

    [Fact]
    public async Task Deleting_needs_no_unit_of_work_either_and_still_soft_deletes()
    {
        await using var database = await StartDatabaseAsync();

        var order = Order.Place("A-4");
        await order.SaveAsync();

        await order.DeleteAsync();

        // Order is ISoftDeletable, so the interceptor stamped it rather than removing the row.
        Assert.Equal(0, await Order.CountAsync());
        Assert.Equal(1, await Order.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Inside_a_unit_of_work_an_insert_and_a_delete_still_wait_for_the_commit()
    {
        await using var database = await StartDatabaseAsync();

        var existing = Order.Place("B-1");
        await existing.SaveAsync();

        await using (var uow = Db.Begin())
        {
            await Order.Place("B-2").SaveAsync();
            await existing.DeleteAsync();

            // Neither has been written: the caller owns the commit.
            Assert.Equal(1, await Order.CountAsync());
        }

        // Disposed without saving, so both were discarded.
        Assert.Equal(1, await Order.CountAsync());
        Assert.Equal(1, await Order.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task The_callers_commit_writes_the_insert_and_the_delete_together()
    {
        await using var database = await StartDatabaseAsync();

        var existing = Order.Place("C-1");
        await existing.SaveAsync();

        await using (var uow = Db.Begin())
        {
            await Order.Place("C-2").SaveAsync();
            await existing.DeleteAsync();
            await uow.SaveChangesAsync();
        }

        var live = await Order.ToListAsync();
        Assert.Single(live);
        Assert.Equal("C-2", live[0].Reference);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));
}
