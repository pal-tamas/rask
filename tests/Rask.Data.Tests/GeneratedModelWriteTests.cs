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

    private readonly InMemorySqlite _sqlite = new();
    private readonly FakeClock _clock = new(Start);

    public void Dispose() => _sqlite.Dispose();

    // ---- create -----------------------------------------------------------------------------------

    [Fact]
    public async Task Create_inserts_the_entity_stamped_from_the_clock()
    {
        await using var database = await StartDatabaseAsync();

        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Start.UtcDateTime, widget.CreatedAt);

        var stored = await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal("anvil", stored.Name);
        Assert.Equal(Start.UtcDateTime, stored.CreatedAt);
        Assert.Equal(0, stored.Version);
    }

    [Fact]
    public async Task A_write_without_a_token_is_cancelled_with_the_work_it_runs_in()
    {
        await using var database = await StartDatabaseAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var work = Ambient.Enter(cancelled.Token);

#pragma warning disable xUnit1051 // the point: a write given no token falls back to the ambient one
        var write = GeneratedModelWrites.Create(Widget.Create("anvil"));
#pragma warning restore xUnit1051

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
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

        var stored = await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken);
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

        var stored = await database.Load<Order>(order.Id, TestContext.Current.CancellationToken);
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
        await Assert.ThrowsAsync<RaskValidationException>(() =>
            GeneratedModelWrites.Update<Widget>(widget.Id, version: 0, w => w.Rename("stale-edit"), cancellationToken: TestContext.Current.CancellationToken));

        var stored = await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken);
        Assert.Equal("first-edit", stored!.Name);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task A_stale_save_is_refused_as_one_failure_about_the_whole_form()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("original"), cancellationToken: TestContext.Current.CancellationToken);
        await Widget.Update(widget.Id, w => w.Rename("first-edit"), version: 0, cancellationToken: TestContext.Current.CancellationToken);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() =>
            Widget.Update(widget.Id, w => w.Rename("stale-edit"), version: 0, cancellationToken: TestContext.Current.CancellationToken));

        var failure = Assert.Single(refused.Failures);
        Assert.Empty(failure.Fields);
        Assert.Equal("Someone else changed this in the meantime. Reload it and try again.", failure.Message);
        Assert.IsType<DbUpdateConcurrencyException>(refused.InnerException);
    }

    [Fact]
    public async Task An_awaited_update_runs_its_change_before_the_save()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("original"), cancellationToken: TestContext.Current.CancellationToken);

        await Widget.Update(widget.Id, async w =>
        {
            var others = await Widget.Count(TestContext.Current.CancellationToken);
            w.Rename($"one of {others}");
        }, version: 0, cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken);
        Assert.Equal("one of 1", stored!.Name);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task Find_at_the_version_the_form_was_filled_from_hands_the_aggregate_back()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("original"), cancellationToken: TestContext.Current.CancellationToken);

        var found = await Widget.Find(widget.Id, version: 0, TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal("original", found.Name);
    }

    [Fact]
    public async Task Find_at_a_version_saved_over_since_is_refused_before_anything_changes()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("original"), cancellationToken: TestContext.Current.CancellationToken);
        await Widget.Update(widget.Id, w => w.Rename("first-edit"), cancellationToken: TestContext.Current.CancellationToken);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() =>
            Widget.Find(widget.Id, version: 0, TestContext.Current.CancellationToken));

        Assert.Equal("Someone else changed this in the meantime. Reload it and try again.", Assert.Single(refused.Failures).Message);
    }

    [Fact]
    public async Task Find_at_a_version_of_a_key_no_row_has_finds_nothing()
    {
        await using var database = await StartDatabaseAsync();

        var found = await Widget.Find(Guid.NewGuid(), version: 0, TestContext.Current.CancellationToken);

        Assert.Null(found);
    }

    [Fact]
    public async Task Update_without_a_version_skips_the_check()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("original"), cancellationToken: TestContext.Current.CancellationToken);
        await GeneratedModelWrites.Update<Widget>(widget.Id, version: 0, w => w.Rename("first-edit"), cancellationToken: TestContext.Current.CancellationToken);

        await GeneratedModelWrites.Update<Widget>(widget.Id, version: null, w => w.Rename("last-edit"), cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken);
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
    public async Task Delete_of_a_key_no_row_ever_had_throws_KeyNotFound_and_writes_nothing()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("kept"), cancellationToken: TestContext.Current.CancellationToken);

        var refused = await Record.ExceptionAsync(() => Widget.Delete(Guid.NewGuid(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.IsType<KeyNotFoundException>(refused);
        var only = await Widget.IgnoreQueryFilters().Single(TestContext.Current.CancellationToken);
        Assert.Equal(widget.Id, only.Id);
        Assert.Equal("kept", only.Name);
        Assert.Equal(0, only.Version);
        Assert.Null(only.DeletedAt);
    }

    [Fact]
    public async Task Delete_at_a_stale_version_is_refused_and_the_row_stays()
    {
        await using var database = await StartDatabaseAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("contested"), cancellationToken: TestContext.Current.CancellationToken);
        await GeneratedModelWrites.Update<Widget>(widget.Id, version: 0, w => w.Rename("edited"), cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<RaskValidationException>(() =>
            GeneratedModelWrites.Delete<Widget>(widget.Id, version: 0, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, await Widget.Count(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_of_an_aggregate_that_is_not_deletable_is_refused_before_anything_is_opened()
    {
        // The generated JournalLine.Delete does not exist; this is the direct call into the write half.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GeneratedModelWrites.Delete<JournalLine>(Guid.NewGuid(), version: null, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("names neither Deletable nor SoftDelete", refused.Message, StringComparison.Ordinal);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), _clock);
}

// An append-only record: corrected by a reversing entry, never removed.
public sealed class JournalLine : Aggregate<Guid, Timestamps, Versioned, Model>
{
    public decimal Amount { get; private set; }
}
