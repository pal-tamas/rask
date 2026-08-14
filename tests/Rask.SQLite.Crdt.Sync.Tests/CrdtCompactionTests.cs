namespace Rask.SQLite.Crdt.Sync.Tests;

/// <summary>
///     Compaction folds a replica's own objects into one. What makes it possible is that the feed is
///     current state rather than history; what makes it safe is that a replica only ever rewrites its
///     own prefix, and that the replacement sorts after everything it replaces.
/// </summary>
public sealed class CrdtCompactionTests
{
    private const string AliceSite = "aa000000000000000000000000000001";
    private const string BobSite = "bb000000000000000000000000000002";

    [Fact]
    public async Task Many_objects_become_one()
    {
        var (bucket, feed, alice) = Engine(AliceSite, new CrdtSyncOptions { MaxChangesPerObject = 1 });

        for (var i = 0; i < 8; i++)
        {
            feed.Write("Todos", "Title", $"item {i}");
        }

        await alice.PushAsync();
        Assert.Equal(8, bucket.Keys.Count);

        var removed = await alice.CompactAsync();

        // Seven, not eight: the replacement covers up to the newest version, so its key is the newest
        // object's key and it replaces that one in place rather than being written beside it.
        Assert.Equal(7, removed);
        Assert.Single(bucket.Keys);
    }

    [Fact]
    public async Task The_replacement_sorts_after_everything_it_replaced()
    {
        // The property the whole design rests on. A peer resumes from the last key it read, so a
        // replacement that sorted BEFORE that key would never be fetched — and because it is the only
        // object left, the peer would silently stop receiving anything from this replica.
        var (bucket, feed, alice) = Engine(AliceSite, new CrdtSyncOptions { MaxChangesPerObject = 1 });

        for (var i = 0; i < 5; i++)
        {
            feed.Write("Todos", "Title", $"item {i}");
        }

        await alice.PushAsync();
        var before = bucket.Keys.Order(StringComparer.Ordinal).ToList();

        await alice.CompactAsync();
        var replacement = Assert.Single(bucket.Keys);

        Assert.All(before, old =>
            Assert.True(string.CompareOrdinal(replacement, old) >= 0,
                $"'{replacement}' must sort at or after the '{old}' it replaced"));
    }

    [Fact]
    public async Task A_peer_that_already_synced_loses_nothing()
    {
        var (bucket, feed, alice) = Engine(AliceSite, new CrdtSyncOptions { MaxChangesPerObject = 1 });
        var bobFeed = new FakeChangeFeed(BobSite);
        var bob = new CrdtSyncEngine(bucket, bobFeed);

        for (var i = 0; i < 5; i++)
        {
            feed.Write("Todos", "Title", $"item {i}");
        }

        await alice.PushAsync();
        await bob.SyncAsync();
        var seenBefore = bobFeed.Log.Count;

        await alice.CompactAsync();
        feed.Write("Todos", "Title", "after compaction");
        await alice.PushAsync();

        await bob.SyncAsync();

        // Bob keeps everything he had and picks up what came after.
        Assert.True(bobFeed.Log.Count >= seenBefore);
        Assert.Contains(bobFeed.Log, c => Equals(c.Value, "after compaction"));
        foreach (var i in Enumerable.Range(0, 5))
        {
            Assert.Contains(bobFeed.Log, c => Equals(c.Value, $"item {i}"));
        }
    }

    [Fact]
    public async Task A_brand_new_peer_gets_the_whole_state_from_one_object()
    {
        // The payoff: a device joining a year-old family does not replay a year of syncs.
        var (bucket, feed, alice) = Engine(AliceSite, new CrdtSyncOptions { MaxChangesPerObject = 1 });

        for (var i = 0; i < 6; i++)
        {
            feed.Write("Todos", "Title", $"item {i}");
        }

        await alice.PushAsync();
        await alice.CompactAsync();

        var newcomerFeed = new FakeChangeFeed(BobSite);
        var newcomer = new CrdtSyncEngine(bucket, newcomerFeed);
        var status = await newcomer.SyncAsync();

        Assert.Equal(6, status.Received);
        Assert.Equal(1, bucket.Gets);
    }

    [Fact]
    public async Task Compacting_twice_changes_nothing_the_second_time()
    {
        var (bucket, feed, alice) = Engine(AliceSite, new CrdtSyncOptions { MaxChangesPerObject = 1 });

        for (var i = 0; i < 4; i++)
        {
            feed.Write("Todos", "Title", $"item {i}");
        }

        await alice.PushAsync();
        await alice.CompactAsync();
        var after = bucket.Keys.ToList();

        Assert.Equal(0, await alice.CompactAsync());
        Assert.Equal(after, bucket.Keys);
    }

    [Fact]
    public async Task Compaction_leaves_another_replicas_prefix_alone()
    {
        // A replica only ever rewrites its own prefix — the same rule that removes write conflicts is
        // what makes compaction a purely local decision, needing no coordination.
        var bucket = new FakeObjectStore();
        var aliceFeed = new FakeChangeFeed(AliceSite);
        var alice = new CrdtSyncEngine(bucket, aliceFeed, null,
            new CrdtSyncOptions { MaxChangesPerObject = 1, CompactAfterObjects = 0 });
        var bobFeed = new FakeChangeFeed(BobSite);
        var bob = new CrdtSyncEngine(bucket, bobFeed, null,
            new CrdtSyncOptions { MaxChangesPerObject = 1, CompactAfterObjects = 0 });

        for (var i = 0; i < 3; i++)
        {
            aliceFeed.Write("Todos", "Title", $"alice {i}");
            bobFeed.Write("Todos", "Title", $"bob {i}");
        }

        await alice.PushAsync();
        await bob.PushAsync();

        var bobsKeys = bucket.Keys.Where(k => k.Contains(BobSite, StringComparison.Ordinal)).ToList();
        await alice.CompactAsync();

        Assert.Equal(bobsKeys, bucket.Keys.Where(k => k.Contains(BobSite, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_sync_compacts_once_the_prefix_grows_past_the_threshold()
    {
        var bucket = new FakeObjectStore();
        var feed = new FakeChangeFeed(AliceSite);
        var alice = new CrdtSyncEngine(bucket, feed, null,
            new CrdtSyncOptions { MaxChangesPerObject = 1, CompactAfterObjects = 3 });

        for (var i = 0; i < 5; i++)
        {
            feed.Write("Todos", "Title", $"item {i}");
        }

        await alice.SyncAsync();

        Assert.Single(bucket.Keys);
    }

    [Fact]
    public async Task Compaction_can_be_turned_off()
    {
        var bucket = new FakeObjectStore();
        var feed = new FakeChangeFeed(AliceSite);
        var alice = new CrdtSyncEngine(bucket, feed, null,
            new CrdtSyncOptions { MaxChangesPerObject = 1, CompactAfterObjects = 0 });

        for (var i = 0; i < 5; i++)
        {
            feed.Write("Todos", "Title", $"item {i}");
        }

        await alice.SyncAsync();

        Assert.Equal(5, bucket.Keys.Count);
    }

    [Fact]
    public async Task Publishing_resumes_correctly_after_compaction()
    {
        // The published watermark has to follow the replacement key, or the next push re-uploads
        // everything the compaction just folded up.
        var (bucket, feed, alice) = Engine(AliceSite, new CrdtSyncOptions { MaxChangesPerObject = 1 });

        for (var i = 0; i < 4; i++)
        {
            feed.Write("Todos", "Title", $"item {i}");
        }

        await alice.PushAsync();
        await alice.CompactAsync();

        Assert.Equal(0, await alice.PushAsync());
        Assert.Single(bucket.Keys);

        feed.Write("Todos", "Title", "new one");
        Assert.Equal(1, await alice.PushAsync());
        Assert.Equal(2, bucket.Keys.Count);
    }

    [Fact]
    public async Task A_single_object_is_left_alone()
    {
        var (bucket, feed, alice) = Engine(AliceSite, new CrdtSyncOptions());
        feed.Write("Todos", "Title", "only one");
        await alice.PushAsync();

        Assert.Equal(0, await alice.CompactAsync());
        Assert.Single(bucket.Keys);
    }

    private static (FakeObjectStore Bucket, FakeChangeFeed Feed, CrdtSyncEngine Engine) Engine(
        string site, CrdtSyncOptions options)
    {
        // Compaction off by default in these, so the explicit CompactAsync calls are what is under test.
        options.CompactAfterObjects = 0;
        var bucket = new FakeObjectStore();
        var feed = new FakeChangeFeed(site);
        return (bucket, feed, new CrdtSyncEngine(bucket, feed, null, options));
    }
}
