using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.Crdt.Sync.Tests;

/// <summary>
///     The engine's other tests run against a fake feed, which encodes an <em>understanding</em> of
///     cr-sqlite. These run two real replicas through a bucket, so the understanding is checked against
///     the extension rather than against itself.
/// </summary>
/// <remarks>
///     cr-sqlite ships a separate native binary per platform and it is not in this repo, so these skip
///     unless <c>RASK_CRSQLITE_PATH</c> points at one.
/// </remarks>
public sealed class RealReplicaSyncTests : IDisposable
{
    private const string SkipReason =
        "Set RASK_CRSQLITE_PATH to cr-sqlite's loadable extension to run the real-replica sync tests.";

    private readonly List<string> _files = [];

    private static string? ExtensionPath => Environment.GetEnvironmentVariable("RASK_CRSQLITE_PATH");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in _files)
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    [SkippableFact]
    public async Task Two_real_replicas_converge_through_a_bucket()
    {
        Skip.If(!Available(), SkipReason);

        var bucket = new FakeObjectStore();
        var id = Guid.NewGuid();

        await using var alice = await NewReplicaAsync(bucket);
        await using var bob = await NewReplicaAsync(bucket);

        alice.Context.Todos.Add(new Todo { Id = id, Title = "milk", Priority = 1 });
        await alice.Context.SaveChangesAsync();

        Assert.Equal(CrdtSyncPhase.Synced, (await alice.Engine.SyncAsync()).Phase);
        var status = await bob.Engine.SyncAsync();

        Assert.Equal(CrdtSyncPhase.Synced, status.Phase);
        Assert.Equal(1, status.Peers);

        bob.Context.ChangeTracker.Clear();
        var arrived = await bob.Context.Todos.SingleAsync(t => t.Id == id);
        Assert.Equal("milk", arrived.Title);
        Assert.Equal(1, arrived.Priority);
    }

    [SkippableFact]
    public async Task Concurrent_edits_to_different_columns_both_survive_a_round_trip()
    {
        Skip.If(!Available(), SkipReason);

        // The claim of the whole stack, end to end and through the bucket rather than hand-fed: per
        // column, not per row.
        var bucket = new FakeObjectStore();
        var id = Guid.NewGuid();

        await using var alice = await NewReplicaAsync(bucket);
        await using var bob = await NewReplicaAsync(bucket);

        alice.Context.Todos.Add(new Todo { Id = id, Title = "draft", Priority = 1 });
        await alice.Context.SaveChangesAsync();
        await alice.Engine.SyncAsync();
        await bob.Engine.SyncAsync();

        var alices = await alice.Context.Todos.SingleAsync(t => t.Id == id);
        alices.Title = "final";
        await alice.Context.SaveChangesAsync();

        var bobs = await bob.Context.Todos.SingleAsync(t => t.Id == id);
        bobs.Priority = 9;
        await bob.Context.SaveChangesAsync();

        await alice.Engine.SyncAsync();
        await bob.Engine.SyncAsync();
        await alice.Engine.SyncAsync();

        foreach (var replica in new[] { alice, bob })
        {
            replica.Context.ChangeTracker.Clear();
            var merged = await replica.Context.Todos.SingleAsync(t => t.Id == id);
            Assert.Equal("final", merged.Title);
            Assert.Equal(9, merged.Priority);
        }
    }

    [SkippableFact]
    public async Task Every_sqlite_storage_class_survives_the_bucket()
    {
        Skip.If(!Available(), SkipReason);

        // The codec is unit-tested against hand-built values; this proves the values a real reader hands
        // back are the ones it was written to handle, which is the assumption that would fail silently.
        var bucket = new FakeObjectStore();
        var id = Guid.NewGuid();

        await using var alice = await NewReplicaAsync(bucket);
        await using var bob = await NewReplicaAsync(bucket);

        alice.Context.Todos.Add(new Todo
        {
            Id = id,
            Title = "text",
            Priority = 7,               // integer
            Score = 2.5,                // real
            Attachment = [1, 2, 3],     // blob
            Notes = null,               // null
            Done = true,
        });
        await alice.Context.SaveChangesAsync();

        await alice.Engine.SyncAsync();
        await bob.Engine.SyncAsync();

        bob.Context.ChangeTracker.Clear();
        var arrived = await bob.Context.Todos.SingleAsync(t => t.Id == id);

        Assert.Equal("text", arrived.Title);
        Assert.Equal(7, arrived.Priority);
        Assert.Equal(2.5, arrived.Score);
        Assert.Equal([1, 2, 3], arrived.Attachment);
        Assert.Null(arrived.Notes);
        Assert.True(arrived.Done);
    }

    [SkippableFact]
    public async Task A_replica_offline_for_a_while_catches_up_in_one_sync()
    {
        Skip.If(!Available(), SkipReason);

        var bucket = new FakeObjectStore();
        await using var alice = await NewReplicaAsync(bucket);
        await using var bob = await NewReplicaAsync(bucket);

        for (var i = 0; i < 5; i++)
        {
            alice.Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = $"item {i}" });
            await alice.Context.SaveChangesAsync();
            await alice.Engine.SyncAsync();
        }

        await bob.Engine.SyncAsync();

        bob.Context.ChangeTracker.Clear();
        Assert.Equal(5, await bob.Context.Todos.CountAsync());

        // And a second sync costs nothing, because the watermark advanced past everything read.
        Assert.Equal(0, (await bob.Engine.SyncAsync()).Received);
    }

    [SkippableFact]
    public async Task Compaction_costs_the_size_of_the_database_not_the_number_of_edits()
    {
        Skip.If(!Available(), SkipReason);

        // The claim compaction rests on, and the one the fake feed cannot model: cr-sqlite's feed holds
        // one entry per (row, column) with the value that won, so editing a field a hundred times leaves
        // ONE entry. If it were a history log, folding a prefix into a single object would be pointless.
        var bucket = new FakeObjectStore();
        await using var alice = await NewReplicaAsync(bucket);

        var id = Guid.NewGuid();
        alice.Context.Todos.Add(new Todo { Id = id, Title = "v1" });
        await alice.Context.SaveChangesAsync();

        var afterInsert = (await new CrdtChangeFeed(alice.Context).ReadLocalChangesAsync()).Count;

        for (var i = 2; i <= 40; i++)
        {
            alice.Context.ChangeTracker.Clear();
            var todo = await alice.Context.Todos.SingleAsync(t => t.Id == id);
            todo.Title = $"v{i}";
            await alice.Context.SaveChangesAsync();
        }

        var afterEdits = await new CrdtChangeFeed(alice.Context).ReadLocalChangesAsync();

        Assert.Equal(afterInsert, afterEdits.Count);
        Assert.Equal("v40", Assert.Single(afterEdits, c => c.ColumnName == "Title").Value);
    }

    [SkippableFact]
    public async Task A_new_device_reaches_the_right_state_from_a_compacted_prefix()
    {
        Skip.If(!Available(), SkipReason);

        var bucket = new FakeObjectStore();
        await using var alice = await NewReplicaAsync(bucket, new CrdtSyncOptions
        {
            MaxChangesPerObject = 1,
            CompactAfterObjects = 0,
        });

        var kept = Guid.NewGuid();
        var removed = Guid.NewGuid();

        alice.Context.Todos.Add(new Todo { Id = kept, Title = "still here", Priority = 2 });
        alice.Context.Todos.Add(new Todo { Id = removed, Title = "deleted later" });
        await alice.Context.SaveChangesAsync();
        await alice.Engine.PushAsync();

        alice.Context.ChangeTracker.Clear();
        alice.Context.Todos.Remove(await alice.Context.Todos.SingleAsync(t => t.Id == removed));
        await alice.Context.SaveChangesAsync();
        await alice.Engine.PushAsync();

        Assert.True(bucket.Keys.Count > 1);
        await alice.Engine.CompactAsync();
        Assert.Single(bucket.Keys);

        // A device that has never seen any of it: the surviving todo arrives, and the deleted one stays
        // deleted — the tombstone has to survive compaction, or the row would come back from the dead.
        await using var newcomer = await NewReplicaAsync(bucket);
        Assert.Equal(CrdtSyncPhase.Synced, (await newcomer.Engine.SyncAsync()).Phase);

        newcomer.Context.ChangeTracker.Clear();
        var todos = await newcomer.Context.Todos.ToListAsync();

        Assert.Equal("still here", Assert.Single(todos).Title);
        Assert.Equal(2, todos[0].Priority);
    }

    private static bool Available() => ExtensionPath is { Length: > 0 } p && File.Exists(p);

    private async Task<Replica> NewReplicaAsync(FakeObjectStore bucket, CrdtSyncOptions? options = null)
    {
        var file = Path.Combine(Path.GetTempPath(), $"rask-crdt-sync-{Guid.NewGuid():N}.db");
        _files.Add(file);
        var connectionString = $"Data Source={file};Pooling=False";

        await using (var plain = new TodoContext(
                         new DbContextOptionsBuilder<TodoContext>().UseSqlite(connectionString).Options))
        {
            await plain.Database.EnsureCreatedAsync();
        }

        var context = new TodoContext(new DbContextOptionsBuilder<TodoContext>()
            .UseSqlite(connectionString)
            .UseRaskCrdt(o => o.ExtensionPath = ExtensionPath!)
            .Options);

        await context.PromoteToCrrsAsync();

        var feed = new CrdtChangeFeed(context);
        return new Replica(context, new CrdtSyncEngine(bucket, feed, null, options));
    }

    private sealed class Replica(TodoContext context, CrdtSyncEngine engine) : IAsyncDisposable
    {
        public TodoContext Context { get; } = context;

        public CrdtSyncEngine Engine { get; } = engine;

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
