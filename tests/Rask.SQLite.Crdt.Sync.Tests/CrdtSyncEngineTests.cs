namespace Rask.SQLite.Crdt.Sync.Tests;

public sealed class CrdtSyncEngineTests
{
    private const string AliceSite = "aa000000000000000000000000000001";
    private const string BobSite = "bb000000000000000000000000000002";

    [Fact]
    public async Task A_device_writes_only_under_its_own_prefix()
    {
        // The rule everything else rests on: if no two devices ever write the same key, there is nothing
        // to lock, nothing to retry on conflict, and no lease to leak when a device disappears.
        var bucket = new FakeObjectStore();
        var alice = Engine(bucket, AliceSite, out var aliceFeed);
        var bob = Engine(bucket, BobSite, out var bobFeed);

        aliceFeed.Write("Todos", "Title", "milk");
        bobFeed.Write("Todos", "Title", "bread");

        await alice.SyncAsync();
        await bob.SyncAsync();

        Assert.All(bucket.Keys, key => Assert.StartsWith("crdt/", key, StringComparison.Ordinal));
        Assert.Contains(bucket.Keys, k => k.StartsWith($"crdt/{AliceSite}/", StringComparison.Ordinal));
        Assert.Contains(bucket.Keys, k => k.StartsWith($"crdt/{BobSite}/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_peers_work_arrives()
    {
        var bucket = new FakeObjectStore();
        var alice = Engine(bucket, AliceSite, out var aliceFeed);
        var bob = Engine(bucket, BobSite, out var bobFeed);

        aliceFeed.Write("Todos", "Title", "milk");
        await alice.SyncAsync();

        var status = await bob.SyncAsync();

        Assert.Equal(CrdtSyncPhase.Synced, status.Phase);
        Assert.Equal(1, status.Received);
        Assert.Equal(1, status.Peers);
        Assert.Contains(bobFeed.Log, c => Equals(c.Value, "milk"));
    }

    [Fact]
    public async Task A_replica_never_reads_back_its_own_prefix()
    {
        // Reading your own objects would re-apply your own history on every sync — harmless, because
        // applying is idempotent, but it would grow the cost of a sync with the size of the database
        // rather than with what changed, which is the whole point of the layout.
        var bucket = new FakeObjectStore();
        var alice = Engine(bucket, AliceSite, out var aliceFeed);

        aliceFeed.Write("Todos", "Title", "milk");
        await alice.SyncAsync();

        var second = await alice.SyncAsync();

        Assert.Equal(0, second.Received);
        Assert.Equal(0, second.Peers);
        Assert.Equal(0, bucket.Gets);
    }

    [Fact]
    public async Task A_second_sync_reads_nothing_new()
    {
        var bucket = new FakeObjectStore();
        var alice = Engine(bucket, AliceSite, out var aliceFeed);
        var bob = Engine(bucket, BobSite, out _);

        aliceFeed.Write("Todos", "Title", "milk");
        await alice.SyncAsync();
        await bob.SyncAsync();

        var readsAfterFirst = bucket.Gets;
        var second = await bob.SyncAsync();

        Assert.Equal(0, second.Received);
        Assert.Equal(readsAfterFirst, bucket.Gets);
    }

    [Fact]
    public async Task Nothing_new_uploads_nothing()
    {
        var bucket = new FakeObjectStore();
        var alice = Engine(bucket, AliceSite, out var aliceFeed);

        aliceFeed.Write("Todos", "Title", "milk");
        await alice.SyncAsync();
        var putsAfterFirst = bucket.Puts;

        await alice.SyncAsync();

        Assert.Equal(putsAfterFirst, bucket.Puts);
    }

    [Fact]
    public async Task Only_this_replicas_own_work_is_published()
    {
        // Bob holds Alice's changes after syncing. If he republished them, every device would carry every
        // other device's history and the bucket would grow with the square of the number of peers.
        var bucket = new FakeObjectStore();
        var alice = Engine(bucket, AliceSite, out var aliceFeed);
        var bob = Engine(bucket, BobSite, out var bobFeed);

        aliceFeed.Write("Todos", "Title", "milk");
        await alice.SyncAsync();
        await bob.SyncAsync();

        bobFeed.Write("Todos", "Title", "bread");
        await bob.SyncAsync();

        var bobsObjects = bucket.Keys.Where(k => k.StartsWith($"crdt/{BobSite}/", StringComparison.Ordinal));
        var published = bobsObjects
            .SelectMany(k => CrdtChangeCodec.Decode(Read(bucket, k)))
            .ToList();

        Assert.NotEmpty(published);
        Assert.All(published, c => Assert.Equal(Convert.FromHexString(BobSite), c.SiteId));
    }

    [Fact]
    public async Task An_unreachable_bucket_is_offline_not_an_exception()
    {
        // Being offline is the operating mode this exists for. Local edits are already committed to
        // SQLite, so there is nothing to lose and nothing for the app to handle.
        var bucket = new FakeObjectStore();
        var alice = Engine(bucket, AliceSite, out var aliceFeed);
        aliceFeed.Write("Todos", "Title", "milk");

        bucket.Offline = true;
        var status = await alice.SyncAsync();

        Assert.Equal(CrdtSyncPhase.Offline, status.Phase);
        Assert.NotNull(status.Error);

        bucket.Offline = false;
        Assert.Equal(CrdtSyncPhase.Synced, (await alice.SyncAsync()).Phase);
        Assert.NotEmpty(bucket.Keys);
    }

    [Fact]
    public async Task A_status_is_reported_for_every_attempt()
    {
        var bucket = new FakeObjectStore();
        var alice = Engine(bucket, AliceSite, out _);
        var seen = new List<CrdtSyncPhase>();
        alice.Changed += s => seen.Add(s.Phase);

        await alice.SyncAsync();

        Assert.Equal([CrdtSyncPhase.Syncing, CrdtSyncPhase.Synced], seen);
    }

    [Fact]
    public async Task A_large_feed_is_split_across_objects()
    {
        var bucket = new FakeObjectStore();
        var feed = new FakeChangeFeed(AliceSite);
        var alice = new CrdtSyncEngine(bucket, feed, null, new CrdtSyncOptions { MaxChangesPerObject = 10 });

        for (var i = 0; i < 25; i++)
        {
            feed.Write("Todos", "Title", $"item {i}");
        }

        var published = await alice.PushAsync();

        Assert.Equal(25, published);
        Assert.Equal(3, bucket.Keys.Count);
    }

    [Fact]
    public async Task A_split_feed_arrives_whole_and_in_order()
    {
        var bucket = new FakeObjectStore();
        var feed = new FakeChangeFeed(AliceSite);
        var alice = new CrdtSyncEngine(bucket, feed, null, new CrdtSyncOptions { MaxChangesPerObject = 10 });
        var bob = Engine(bucket, BobSite, out var bobFeed);

        for (var i = 0; i < 25; i++)
        {
            feed.Write("Todos", "Title", $"item {i}");
        }

        await alice.PushAsync();
        var status = await bob.SyncAsync();

        Assert.Equal(25, status.Received);
        Assert.Equal(
            Enumerable.Range(0, 25).Select(i => $"item {i}"),
            bobFeed.Log.Select(c => c.Value));
    }

    [Fact]
    public async Task A_reinstalled_device_does_not_republish_its_history()
    {
        // The state store is a cache, not a record — losing it must cost requests, never a re-upload of
        // everything the database has ever contained.
        var bucket = new FakeObjectStore();
        var feed = new FakeChangeFeed(AliceSite);

        var before = new CrdtSyncEngine(bucket, feed);
        feed.Write("Todos", "Title", "milk");
        await before.SyncAsync();
        var keysAfterFirst = bucket.Keys.ToList();
        var putsAfterFirst = bucket.Puts;

        // Same database, brand new sync state.
        var after = new CrdtSyncEngine(bucket, feed, new InMemoryCrdtSyncStore());
        await after.SyncAsync();

        Assert.Equal(keysAfterFirst, bucket.Keys);
        Assert.Equal(putsAfterFirst, bucket.Puts);
    }

    [Fact]
    public async Task An_object_that_vanished_between_listing_and_reading_is_skipped()
    {
        var bucket = new FakeObjectStore();
        var alice = Engine(bucket, AliceSite, out var aliceFeed);
        var bob = Engine(bucket, BobSite, out _);

        aliceFeed.Write("Todos", "Title", "milk");
        await alice.SyncAsync();

        var vanishing = new VanishingStore(bucket);
        var bobAgain = new CrdtSyncEngine(vanishing, new FakeChangeFeed(BobSite));

        var status = await bobAgain.SyncAsync();

        Assert.Equal(CrdtSyncPhase.Synced, status.Phase);
        Assert.Equal(0, status.Received);
    }

    [Theory]
    [InlineData("crdt/aa/changes/0000000000000001__000000000000000a.json", 10L)]
    [InlineData("crdt/aa/changes/0000000000000000__0000000000000000.json", 0L)]
    public void The_upper_bound_is_read_from_the_key(string key, long expected)
    {
        Assert.Equal(expected, CrdtSyncEngine.ParseUpperBound(key));
    }

    [Theory]
    [InlineData("crdt/aa/changes/nonsense.json")]
    [InlineData("crdt/aa/changes/0001__0002.bin")]
    public void A_foreign_key_shape_is_refused(string key)
    {
        // Somebody else's object under our prefix, or a format from a future version. Guessing at it
        // would produce a wrong watermark, which silently skips changes.
        Assert.Throws<InvalidOperationException>(() => CrdtSyncEngine.ParseUpperBound(key));
    }

    [Fact]
    public void A_batch_size_of_zero_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new CrdtSyncEngine(new FakeObjectStore(), new FakeChangeFeed(AliceSite), null,
                new CrdtSyncOptions { MaxChangesPerObject = 0 }));
    }

    [Fact]
    public async Task A_prefix_without_a_trailing_slash_still_separates_devices()
    {
        var bucket = new FakeObjectStore();
        var feed = new FakeChangeFeed(AliceSite);
        var alice = new CrdtSyncEngine(bucket, feed, null, new CrdtSyncOptions { Prefix = "family" });

        feed.Write("Todos", "Title", "milk");
        await alice.SyncAsync();

        Assert.All(bucket.Keys, k => Assert.StartsWith($"family/{AliceSite}/", k, StringComparison.Ordinal));
    }

    private static CrdtSyncEngine Engine(FakeObjectStore bucket, string site, out FakeChangeFeed feed)
    {
        feed = new FakeChangeFeed(site);
        return new CrdtSyncEngine(bucket, feed);
    }

    private static byte[] Read(FakeObjectStore bucket, string key)
    {
        using var stream = bucket.OpenReadAsync(key).GetAwaiter().GetResult()!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Lists an object and then loses it, as a peer compacting mid-sync would.</summary>
    private sealed class VanishingStore(FakeObjectStore inner) : ObjectStore.IObjectStore
    {
        public Task<byte[]?> GetRangeAsync(string key, long offset, int count, CancellationToken ct = default) =>
            inner.GetRangeAsync(key, offset, count, ct);

        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task PutAsync(string key, byte[] content, CancellationToken ct = default) =>
            inner.PutAsync(key, content, ct);

        public Task PutAsync(string key, Stream content, long length, CancellationToken ct = default) =>
            inner.PutAsync(key, content, length, ct);

        public Task<bool> TryCreateAsync(string key, byte[] content, CancellationToken ct = default) =>
            inner.TryCreateAsync(key, content, ct);

        public Task<IReadOnlyList<ObjectStore.ObjectEntry>> ListAsync(
            string prefix, string? startAfter = null, CancellationToken ct = default) =>
            inner.ListAsync(prefix, startAfter, ct);

        public Task<IReadOnlyList<string>> ListPrefixesAsync(string prefix, CancellationToken ct = default) =>
            inner.ListPrefixesAsync(prefix, ct);

        public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(key, ct);
    }
}
