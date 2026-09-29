using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// GeneratedModelWrites is the persistence half that the generated Product.Create / Update /
// Delete call into. These drive it directly, against a real SQLite file with the interceptors the
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

        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Start.UtcDateTime, widget.CreatedAt);

        var stored = await database.LoadAsync<Widget>(widget.Id, TestContext.Current.CancellationToken);
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
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);

        _clock.UtcNow = Start.AddHours(2);
        var updated = await GeneratedModelWrites.Update<Widget>(widget.Id, version: 0, w => w.Rename("hammer"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, updated.Version);

        var stored = await database.LoadAsync<Widget>(widget.Id, TestContext.Current.CancellationToken);
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
        var order = await GeneratedModelWrites.Create(Order.Place("A-1"), cancellationToken: TestContext.Current.CancellationToken);

        await GeneratedModelWrites.Update<Order>(order.Id, version: null, loaded =>
        {
            database.Context.Set<Order>()
                .Where(o => o.Id == order.Id)
                .ExecuteUpdate(s => s.SetProperty(o => o.Reference, "A-1-renamed"));

            loaded.Cancel(Start.UtcDateTime);
        }, cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.LoadAsync<Order>(order.Id, TestContext.Current.CancellationToken);
        Assert.Equal(OrderStatus.Cancelled, stored!.Status);
        Assert.Equal("A-1-renamed", stored.Reference);
    }

    [Fact]
    public async Task Update_at_a_stale_version_is_refused_and_leaves_the_other_writers_row()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("original"), cancellationToken: TestContext.Current.CancellationToken);
        await GeneratedModelWrites.Update<Widget>(widget.Id, version: 0, w => w.Rename("first-edit"), cancellationToken: TestContext.Current.CancellationToken);

        // Still holding version 0, from before the first edit.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            GeneratedModelWrites.Update<Widget>(widget.Id, version: 0, w => w.Rename("stale-edit"), cancellationToken: TestContext.Current.CancellationToken));

        var stored = await database.LoadAsync<Widget>(widget.Id, TestContext.Current.CancellationToken);
        Assert.Equal("first-edit", stored!.Name);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task Update_without_a_version_skips_the_check()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("original"), cancellationToken: TestContext.Current.CancellationToken);
        await GeneratedModelWrites.Update<Widget>(widget.Id, version: 0, w => w.Rename("first-edit"), cancellationToken: TestContext.Current.CancellationToken);

        await GeneratedModelWrites.Update<Widget>(widget.Id, version: null, w => w.Rename("last-edit"), cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.LoadAsync<Widget>(widget.Id, TestContext.Current.CancellationToken);
        Assert.Equal("last-edit", stored!.Name);
        Assert.Equal(2, stored.Version);
    }

    [Fact]
    public async Task Update_of_a_key_no_row_has_throws_KeyNotFound()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            GeneratedModelWrites.Update<Widget>(Guid.NewGuid(), version: null, _ => { }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_soft_deleted_row_is_not_there_to_update_or_delete_again()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("doomed"), cancellationToken: TestContext.Current.CancellationToken);
        await GeneratedModelWrites.Delete<Widget>(widget.Id, version: null, cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            GeneratedModelWrites.Update<Widget>(widget.Id, version: null, w => w.Rename("revived"), cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            GeneratedModelWrites.Delete<Widget>(widget.Id, version: null, cancellationToken: TestContext.Current.CancellationToken));
    }

    // ---- delete -----------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_soft_deletes_an_aggregate()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("doomed"), cancellationToken: TestContext.Current.CancellationToken);

        _clock.UtcNow = Start.AddHours(1);
        await GeneratedModelWrites.Delete<Widget>(widget.Id, version: 0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, await Widget.Count(TestContext.Current.CancellationToken));

        var stored = await Widget.IgnoreQueryFilters().First(w => w.Id == widget.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(Start.AddHours(1).UtcDateTime, stored.DeletedAt);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task Delete_at_a_stale_version_is_refused_and_the_row_stays()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("contested"), cancellationToken: TestContext.Current.CancellationToken);
        await GeneratedModelWrites.Update<Widget>(widget.Id, version: 0, w => w.Rename("edited"), cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            GeneratedModelWrites.Delete<Widget>(widget.Id, version: 0, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, await Widget.Count(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_of_a_Deletion_None_aggregate_is_refused_before_anything_is_opened()
    {
        // The generated JournalLine.Delete does not exist; this is the direct call into the write half.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GeneratedModelWrites.Delete<JournalLine>(Guid.NewGuid(), version: null, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Deletion.None", refused.Message, StringComparison.Ordinal);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);
}

// An append-only record: corrected by a reversing entry, never removed.
public sealed class JournalLine : Aggregate<Guid>
{
    public const Deletion Deletes = Deletion.None;

    public decimal Amount { get; private set; }
}
