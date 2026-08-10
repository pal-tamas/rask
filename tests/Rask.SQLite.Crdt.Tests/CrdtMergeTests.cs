using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.Crdt.Tests;

/// <summary>
///     The claim this package exists to make: two replicas written independently merge without a server,
///     and they merge per <b>column</b>, so two devices editing different fields of the same record both
///     keep their work.
/// </summary>
/// <remarks>
///     cr-sqlite ships a separate native binary per platform and it is not in this repo, so these skip
///     unless <c>RASK_CRSQLITE_PATH</c> points at one — the same opt-in shape as the CLI's build gates.
///     Everything reachable without it is covered by the other test classes, which always run.
/// </remarks>
public sealed class CrdtMergeTests : IDisposable
{
    private const string SkipReason =
        "Set RASK_CRSQLITE_PATH to cr-sqlite's loadable extension (crsqlite.dylib/.so/.dll) to run the merge tests.";

    private readonly List<string> _files = [];

    private static string? ExtensionPath => Environment.GetEnvironmentVariable("RASK_CRSQLITE_PATH");

    public void Dispose()
    {
        // Pools are process-global, and a pooled handle keeps the file open on Windows.
        SqliteConnection.ClearAllPools();

        foreach (var file in _files)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a green run over.
            }
        }
    }

    [SkippableFact]
    public async Task Edits_to_different_columns_of_one_row_both_survive()
    {
        Skip.If(!Available(), SkipReason);

        var id = Guid.NewGuid();
        await using var alice = await NewReplicaAsync();
        await using var bob = await NewReplicaAsync();

        alice.Context.Todos.Add(new Todo { Id = id, Title = "draft", Priority = 1 });
        await alice.Context.SaveChangesAsync();

        // Bob starts from Alice's state, so both are editing a row they agree on. Without this the test
        // would prove only that two inserts survive, which is a far weaker claim.
        await SyncAsync(from: alice, to: bob);

        var bobsCopy = await bob.Context.Todos.SingleAsync(t => t.Id == id);
        Assert.Equal("draft", bobsCopy.Title);

        // The concurrent edit: different columns, neither device aware of the other.
        var alicesCopy = await alice.Context.Todos.SingleAsync(t => t.Id == id);
        alicesCopy.Title = "final";
        await alice.Context.SaveChangesAsync();

        bobsCopy.Priority = 9;
        await bob.Context.SaveChangesAsync();

        await SyncAsync(from: alice, to: bob);
        await SyncAsync(from: bob, to: alice);

        foreach (var replica in new[] { alice, bob })
        {
            replica.Context.ChangeTracker.Clear();
            var merged = await replica.Context.Todos.SingleAsync(t => t.Id == id);

            Assert.Equal("final", merged.Title);
            Assert.Equal(9, merged.Priority);
        }
    }

    [SkippableFact]
    public async Task Applying_the_same_changes_twice_changes_nothing()
    {
        Skip.If(!Available(), SkipReason);

        // This is what makes re-sending safe after an upload whose outcome is unknown, so it is worth
        // pinning rather than assuming from the CRDT label.
        var id = Guid.NewGuid();
        await using var alice = await NewReplicaAsync();
        await using var bob = await NewReplicaAsync();

        alice.Context.Todos.Add(new Todo { Id = id, Title = "once", Priority = 3 });
        await alice.Context.SaveChangesAsync();

        var changes = await alice.Feed.ReadChangesAsync();
        await bob.Feed.ApplyChangesAsync(changes);
        await bob.Feed.ApplyChangesAsync(changes);

        bob.Context.ChangeTracker.Clear();
        var todo = await bob.Context.Todos.SingleAsync(t => t.Id == id);
        Assert.Equal("once", todo.Title);
        Assert.Equal(3, todo.Priority);
        Assert.Equal(1, await bob.Context.Todos.CountAsync());
    }

    [SkippableFact]
    public async Task A_replica_reports_its_own_identity_and_version()
    {
        Skip.If(!Available(), SkipReason);

        await using var alice = await NewReplicaAsync();
        await using var bob = await NewReplicaAsync();

        // Two replicas sharing a site id would attribute each other's writes to themselves, which breaks
        // merging in a way that only shows up once they meet.
        Assert.NotEqual(await alice.Feed.GetSiteIdAsync(), await bob.Feed.GetSiteIdAsync());

        var before = await alice.Feed.GetDbVersionAsync();
        alice.Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = "x" });
        await alice.Context.SaveChangesAsync();

        Assert.True(await alice.Feed.GetDbVersionAsync() > before);
    }

    [SkippableFact]
    public async Task Only_changes_after_a_watermark_are_read()
    {
        Skip.If(!Available(), SkipReason);

        // Reading from a watermark is what makes a sync cost what changed rather than what exists.
        await using var alice = await NewReplicaAsync();

        alice.Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = "first" });
        await alice.Context.SaveChangesAsync();
        var watermark = await alice.Feed.GetDbVersionAsync();

        alice.Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = "second" });
        await alice.Context.SaveChangesAsync();

        var since = await alice.Feed.ReadChangesAsync(watermark);
        Assert.NotEmpty(since);
        Assert.All(since, c => Assert.True(c.DbVersion > watermark));
        Assert.True(since.Count < (await alice.Feed.ReadChangesAsync()).Count);
    }

    private static bool Available() =>
        ExtensionPath is { Length: > 0 } path && File.Exists(path);

    private static async Task SyncAsync(Replica from, Replica to) =>
        await to.Feed.ApplyChangesAsync(await from.Feed.ReadChangesAsync());

    private async Task<Replica> NewReplicaAsync()
    {
        var file = Path.Combine(Path.GetTempPath(), $"rask-crdt-{Guid.NewGuid():N}.db");
        _files.Add(file);

        // Pooling off: cr-sqlite keeps per-connection state, and a handle returned to the pool mid-state
        // and handed to somebody else corrupts quietly rather than failing.
        var connectionString = $"Data Source={file};Pooling=False";

        // Order matters and the failure is silent otherwise: loading cr-sqlite seeds its own bookkeeping
        // tables, and EnsureCreated treats a database that already has tables as provisioned — so creating
        // the schema through a context that loads the extension creates nothing at all, and the first sign
        // of trouble is the promotion complaining that a table has no primary key.
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

        return new Replica(context);
    }

    private sealed class Replica(TodoContext context) : IAsyncDisposable
    {
        public TodoContext Context { get; } = context;

        public CrdtChangeFeed Feed { get; } = new(context);

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
