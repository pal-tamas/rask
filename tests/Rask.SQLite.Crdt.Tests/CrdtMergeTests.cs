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

    [SkippableFact]
    public async Task An_applied_change_keeps_the_originators_site_id()
    {
        Skip.If(!Available(), SkipReason);

        // Load-bearing for publishing: a replica's feed carries every change it ever accepted, and the
        // only thing distinguishing "mine" from "a peer's" is that the site id travels with the change.
        // If application re-stamped it, every device would re-upload every other device's history.
        await using var alice = await NewReplicaAsync();
        await using var bob = await NewReplicaAsync();

        var aliceSite = await alice.Feed.GetSiteIdAsync();
        alice.Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = "from alice" });
        await alice.Context.SaveChangesAsync();

        await SyncAsync(from: alice, to: bob);

        var inBob = await bob.Feed.ReadChangesAsync();
        Assert.All(inBob, c => Assert.Equal(aliceSite, c.SiteId));
        Assert.Empty(await bob.Feed.ReadLocalChangesAsync());
    }

    [SkippableFact]
    public async Task Publishing_reads_only_what_this_replica_originated()
    {
        Skip.If(!Available(), SkipReason);

        await using var alice = await NewReplicaAsync();
        await using var bob = await NewReplicaAsync();

        alice.Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = "from alice" });
        await alice.Context.SaveChangesAsync();
        await SyncAsync(from: alice, to: bob);

        bob.Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = "from bob" });
        await bob.Context.SaveChangesAsync();

        var bobSite = await bob.Feed.GetSiteIdAsync();
        var toPublish = await bob.Feed.ReadLocalChangesAsync();
        var everything = await bob.Feed.ReadChangesAsync();

        Assert.NotEmpty(toPublish);
        Assert.All(toPublish, c => Assert.Equal(bobSite, c.SiteId));
        Assert.True(toPublish.Count < everything.Count, "the unfiltered feed must still carry alice's work");
    }

    [SkippableFact]
    public async Task A_db_version_belongs_to_the_database_it_was_read_from()
    {
        Skip.If(!Available(), SkipReason);

        // The reason a peer watermark cannot be a db_version: applying a change stamps it with the
        // RECEIVING replica's next version, so "everything peer X has after N" is unanswerable from
        // versions alone and the transport has to remember what it already fetched.
        await using var alice = await NewReplicaAsync();
        await using var bob = await NewReplicaAsync();

        // Give bob some history of his own first, so his clock is demonstrably ahead of alice's.
        bob.Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = "bob was here" });
        await bob.Context.SaveChangesAsync();

        alice.Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = "from alice" });
        await alice.Context.SaveChangesAsync();

        var asAlice = await alice.Feed.ReadChangesAsync();
        await SyncAsync(from: alice, to: bob);

        var aliceSite = await alice.Feed.GetSiteIdAsync();
        var asBob = (await bob.Feed.ReadChangesAsync()).Where(c => c.SiteId.SequenceEqual(aliceSite)).ToList();

        Assert.Equal(asAlice.Count, asBob.Count);
        Assert.True(
            asBob.Max(c => c.DbVersion) > asAlice.Max(c => c.DbVersion),
            "the same change carries the receiving replica's version, not the originator's");
    }

    [SkippableFact]
    public async Task A_batch_costs_one_version_not_one_per_change()
    {
        Skip.If(!Available(), SkipReason);

        // Applying row by row would inflate the receiver's version by the size of every batch it ever
        // takes, which is a cost that compounds for the lifetime of the database.
        await using var alice = await NewReplicaAsync();
        await using var bob = await NewReplicaAsync();

        alice.Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = "wide row" });
        await alice.Context.SaveChangesAsync();

        var changes = await alice.Feed.ReadChangesAsync();
        Assert.True(changes.Count > 1, "the model must have several columns for this to mean anything");

        var before = await bob.Feed.GetDbVersionAsync();
        await bob.Feed.ApplyChangesAsync(changes);
        var after = await bob.Feed.GetDbVersionAsync();

        Assert.True(
            after - before <= 1,
            $"applying {changes.Count} changes moved the version by {after - before}; expected one transaction");
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
