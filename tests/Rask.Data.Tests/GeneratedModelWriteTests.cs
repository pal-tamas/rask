using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// GeneratedModelWrites is the persistence half that the generated Product.CreateAsync / UpdateAsync /
// DeleteAsync call into. These drive it directly, against a real SQLite file with the interceptors the
// fixture wires, so what is pinned is what every generated write inherits: one context per call, the
// change tracker in the middle (stamps, versions, soft delete), and the optimistic-concurrency check a
// caller's version turns on.
[Collection(DataDbCollection.Name)]
public sealed class GeneratedModelWriteTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-writes-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(Start);

    public void Dispose() => File.Delete(_dbPath);

    // ---- create -----------------------------------------------------------------------------------

    [Fact]
    public async Task Create_inserts_the_entity_stamped_from_the_clock()
    {
        await using var database = await StartDatabaseAsync();

        var widget = await GeneratedModelWrites.CreateAsync(Widget.Create("anvil"));

        Assert.Equal(Start.UtcDateTime, widget.CreatedAt);

        var stored = await Widget.FindAsync(widget.Id);
        Assert.NotNull(stored);
        Assert.Equal("anvil", stored.Name);
        Assert.Equal(Start.UtcDateTime, stored.CreatedAt);
        Assert.Equal(0, stored.Version);
    }

    // ---- update -----------------------------------------------------------------------------------

    [Fact]
    public async Task Update_writes_the_change_and_bumps_Version_and_UpdatedAt()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.CreateAsync(Widget.Create("anvil"));

        _clock.UtcNow = Start.AddHours(2);
        var updated = await GeneratedModelWrites.UpdateAsync<Widget>(widget.Id, version: 0, w => w.Rename("hammer"));

        Assert.Equal(1, updated.Version);

        var stored = await Widget.FindAsync(widget.Id);
        Assert.Equal("hammer", stored!.Name);
        Assert.Equal(1, stored.Version);
        Assert.Equal(Start.AddHours(2).UtcDateTime, stored.UpdatedAt);
        Assert.Equal(Start.UtcDateTime, stored.CreatedAt);
    }

    [Fact]
    public async Task Update_writes_only_the_columns_that_changed()
    {
        // Another writer renames the order between this write's load and its save. Writing only what
        // `apply` changed is what keeps their rename; writing the whole loaded row back would undo it.
        await using var database = await StartDatabaseAsync();
        var order = await GeneratedModelWrites.CreateAsync(Order.Place("A-1"));

        await GeneratedModelWrites.UpdateAsync<Order>(order.Id, version: null, loaded =>
        {
            database.Context.Set<Order>()
                .Where(o => o.Id == order.Id)
                .ExecuteUpdate(s => s.SetProperty(o => o.Reference, "A-1-renamed"));

            loaded.Cancel(Start.UtcDateTime);
        });

        var stored = await Order.FindAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, stored!.Status);
        Assert.Equal("A-1-renamed", stored.Reference);
    }

    [Fact]
    public async Task Update_at_a_stale_version_is_refused_and_leaves_the_other_writers_row()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.CreateAsync(Widget.Create("original"));
        await GeneratedModelWrites.UpdateAsync<Widget>(widget.Id, version: 0, w => w.Rename("first-edit"));

        // Still holding version 0, from before the first edit.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            GeneratedModelWrites.UpdateAsync<Widget>(widget.Id, version: 0, w => w.Rename("stale-edit")));

        var stored = await Widget.FindAsync(widget.Id);
        Assert.Equal("first-edit", stored!.Name);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task Update_without_a_version_skips_the_check()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.CreateAsync(Widget.Create("original"));
        await GeneratedModelWrites.UpdateAsync<Widget>(widget.Id, version: 0, w => w.Rename("first-edit"));

        await GeneratedModelWrites.UpdateAsync<Widget>(widget.Id, version: null, w => w.Rename("last-edit"));

        var stored = await Widget.FindAsync(widget.Id);
        Assert.Equal("last-edit", stored!.Name);
        Assert.Equal(2, stored.Version);
    }

    [Fact]
    public async Task Update_of_a_key_no_row_has_throws_KeyNotFound()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            GeneratedModelWrites.UpdateAsync<Widget>(Guid.NewGuid(), version: null, _ => { }));
    }

    [Fact]
    public async Task A_soft_deleted_row_is_not_there_to_update_or_delete_again()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.CreateAsync(Widget.Create("doomed"));
        await GeneratedModelWrites.DeleteAsync<Widget>(widget.Id, version: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            GeneratedModelWrites.UpdateAsync<Widget>(widget.Id, version: null, w => w.Rename("revived")));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            GeneratedModelWrites.DeleteAsync<Widget>(widget.Id, version: null));
    }

    // ---- delete -----------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_soft_deletes_an_ISoftDeletable()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.CreateAsync(Widget.Create("doomed"));

        _clock.UtcNow = Start.AddHours(1);
        await GeneratedModelWrites.DeleteAsync<Widget>(widget.Id, version: 0);

        Assert.Equal(0, await Widget.CountAsync());

        var stored = await Widget.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.Id == widget.Id);
        Assert.NotNull(stored);
        Assert.Equal(Start.AddHours(1).UtcDateTime, stored.DeletedAt);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task Delete_really_deletes_a_model_that_is_not_soft_deletable()
    {
        await using var database = await StartDatabaseAsync();
        var doodad = await GeneratedModelWrites.CreateAsync(Doodad.Create("plain"));

        await GeneratedModelWrites.DeleteAsync<Doodad>(doodad.Id, version: null);

        Assert.Equal(0, await Doodad.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Delete_at_a_stale_version_is_refused_and_the_row_stays()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.CreateAsync(Widget.Create("contested"));
        await GeneratedModelWrites.UpdateAsync<Widget>(widget.Id, version: 0, w => w.Rename("edited"));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            GeneratedModelWrites.DeleteAsync<Widget>(widget.Id, version: 0));

        Assert.Equal(1, await Widget.CountAsync());
    }

    // ---- misuse -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_version_for_a_model_that_has_none_is_refused_rather_than_ignored()
    {
        // Order is not IVersioned. Silently skipping the check would let a caller believe it was guarded.
        await using var database = await StartDatabaseAsync();
        var order = await GeneratedModelWrites.CreateAsync(Order.Place("A-1"));

        var update = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GeneratedModelWrites.UpdateAsync<Order>(order.Id, version: 3, o => o.Ship()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GeneratedModelWrites.DeleteAsync<Order>(order.Id, version: 3));

        Assert.Contains(nameof(IVersioned), update.Message, StringComparison.Ordinal);

        var stored = await Order.FindAsync(order.Id);
        Assert.Equal(OrderStatus.Open, stored!.Status);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);
}
