namespace Rask.Sync.Client.Tests;

// Two devices, one bucket, no server. These drive the engine the way an app would — record, sync, go
// offline, come back — and assert the things a user would notice: my edit reached the other device, my
// offline work was not lost, and I was told when something of mine was overwritten.
public class SyncEngineTests
{
    private static readonly Guid Row = new("7f3a2b91-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Base = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static Device NewDevice(FakeObjectStore bucket, string id, long startMs = 0) =>
        new(bucket, id, startMs);

    [Fact]
    public async Task Recording_does_not_touch_the_network()
    {
        var bucket = new FakeObjectStore { Offline = true };
        var a = NewDevice(bucket, "a");

        await a.Engine.RecordAsync(Op(a, "title", "\"offline edit\""));

        Assert.Equal("\"offline edit\"", a.State.Get("Todo", Row)!.Values["title"]);
        Assert.Equal(1, a.Engine.Status.Pending);
    }

    [Fact]
    public async Task An_edit_on_one_device_reaches_another()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");
        var b = NewDevice(bucket, "b");

        await a.Engine.RecordAsync(Op(a, "title", "\"from A\""));
        await a.Engine.SyncAsync();
        await b.Engine.SyncAsync();

        Assert.Equal("\"from A\"", b.State.Get("Todo", Row)!.Values["title"]);
    }

    [Fact]
    public async Task Two_devices_editing_different_fields_both_keep_their_work()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");
        var b = NewDevice(bucket, "b", startMs: 1000);

        await a.Engine.RecordAsync(Op(a, "title", "\"from A\""));
        await b.Engine.RecordAsync(Op(b, "done", "true"));

        await a.Engine.SyncAsync();
        await b.Engine.SyncAsync();
        await a.Engine.SyncAsync();

        foreach (var device in new[] { a, b })
        {
            var row = device.State.Get("Todo", Row)!;
            Assert.Equal("\"from A\"", row.Values["title"]);
            Assert.Equal("true", row.Values["done"]);
        }
    }

    // A device does not read its own operations back. Harmless if it did — replay is idempotent — but it
    // would double the read cost of every sync for no gain.
    [Fact]
    public async Task A_device_does_not_read_back_its_own_prefix()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");

        await a.Engine.RecordAsync(Op(a, "title", "\"mine\""));
        await a.Engine.SyncAsync();
        var getsAfterFirst = bucket.Gets;
        await a.Engine.SyncAsync();

        Assert.Equal(getsAfterFirst, bucket.Gets);
    }

    // The watermark is the whole reason keys sort in clock order. Without it every sync re-reads the
    // entire history, and the cost of syncing grows with how long the app has existed.
    [Fact]
    public async Task A_second_sync_does_not_re_read_what_it_already_has()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");
        var b = NewDevice(bucket, "b");

        await a.Engine.RecordAsync(Op(a, "title", "\"one\""));
        await a.Engine.SyncAsync();

        await b.Engine.SyncAsync();
        var afterFirstPull = bucket.Gets;
        await b.Engine.SyncAsync();

        Assert.Equal(afterFirstPull, bucket.Gets);
    }

    [Fact]
    public async Task New_operations_after_the_watermark_are_still_picked_up()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");
        var b = NewDevice(bucket, "b");

        await a.Engine.RecordAsync(Op(a, "title", "\"one\""));
        await a.Engine.SyncAsync();
        await b.Engine.SyncAsync();

        await a.Engine.RecordAsync(Op(a, "title", "\"two\""));
        await a.Engine.SyncAsync();
        await b.Engine.SyncAsync();

        Assert.Equal("\"two\"", b.State.Get("Todo", Row)!.Values["title"]);
    }

    [Fact]
    public async Task Going_offline_is_a_state_not_an_error_and_keeps_the_queue()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");

        await a.Engine.RecordAsync(Op(a, "title", "\"written offline\""));
        bucket.Offline = true;
        await a.Engine.SyncAsync();

        Assert.Equal(SyncPhase.Offline, a.Engine.Status.Phase);
        Assert.Null(a.Engine.Status.Error);
        Assert.Equal(1, a.Engine.Status.Pending);
    }

    // The point of queueing: work done with no connectivity must still arrive, without the user doing
    // anything or knowing anything happened.
    [Fact]
    public async Task Work_done_offline_uploads_once_connectivity_returns()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");
        var b = NewDevice(bucket, "b");

        bucket.Offline = true;
        await a.Engine.RecordAsync(Op(a, "title", "\"survived\""));
        await a.Engine.SyncAsync();

        bucket.Offline = false;
        await a.Engine.SyncAsync();
        await b.Engine.SyncAsync();

        Assert.Equal(0, a.Engine.Status.Pending);
        Assert.Equal("\"survived\"", b.State.Get("Todo", Row)!.Values["title"]);
    }

    // A failed upload must not clear the queue, or the edit is gone with nothing reporting it.
    [Fact]
    public async Task A_failed_upload_leaves_the_queue_intact()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");

        await a.Engine.RecordAsync(Op(a, "title", "\"t\""));
        bucket.Offline = true;
        await a.Engine.SyncAsync();
        await a.Engine.SyncAsync();

        bucket.Offline = false;
        await a.Engine.SyncAsync();

        Assert.Equal(0, a.Engine.Status.Pending);
        Assert.Single(bucket.Keys, k => k.StartsWith("clients/a/ops/", StringComparison.Ordinal));
    }

    // A reload before a sync is the case that loses data if the queue is not durable — and the local view
    // has to come back too, or the user sees their own edit vanish.
    [Fact]
    public async Task A_reload_before_syncing_keeps_both_the_queue_and_the_local_view()
    {
        var bucket = new FakeObjectStore();
        var store = new InMemorySyncStore();

        var first = new Device(bucket, "a", 0, store);
        await first.Engine.RecordAsync(Op(first, "title", "\"unsynced\""));

        // A new engine over the same local store is what a page reload looks like.
        var reloaded = new Device(bucket, "a", 0, store);
        await reloaded.Engine.SyncAsync();

        Assert.Equal("\"unsynced\"", reloaded.State.Get("Todo", Row)!.Values["title"]);
        Assert.Equal(0, reloaded.Engine.Status.Pending);
    }

    [Fact]
    public async Task A_conflict_from_a_peer_is_surfaced_on_the_status()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");
        var b = NewDevice(bucket, "b", startMs: 5000);

        await a.Engine.RecordAsync(Op(a, "title", "\"from A\""));
        await a.Engine.SyncAsync();

        await b.Engine.RecordAsync(Op(b, "title", "\"from B\""));
        await b.Engine.SyncAsync();
        await a.Engine.SyncAsync();

        Assert.Equal(1, a.Engine.Status.Conflicts);
        var conflict = Assert.Single(a.Engine.Conflicts);
        Assert.Equal("\"from A\"", conflict.LosingValue);
        Assert.Equal("\"from B\"", conflict.WinningValue);
    }

    [Fact]
    public async Task Status_changes_are_announced()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");
        var seen = new List<SyncStatus>();
        a.Engine.Changed += seen.Add;

        await a.Engine.RecordAsync(Op(a, "title", "\"t\""));
        await a.Engine.SyncAsync();

        Assert.Contains(seen, s => s.Pending == 1);
        Assert.Contains(seen, s => s.Phase == SyncPhase.Syncing);
        Assert.Equal(SyncPhase.Idle, seen[^1].Phase);
        Assert.Equal(0, seen[^1].Pending);
    }

    [Fact]
    public async Task Peers_are_counted_and_the_device_never_counts_itself()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");
        var b = NewDevice(bucket, "b");

        await a.Engine.RecordAsync(Op(a, "title", "\"t\""));
        await a.Engine.SyncAsync();
        await b.Engine.RecordAsync(Op(b, "done", "true"));
        await b.Engine.SyncAsync();
        await a.Engine.SyncAsync();

        Assert.Equal(1, a.Engine.Status.Peers);
    }

    // Compaction can delete an object between a peer listing it and reading it. Whatever it held is
    // already applied or lives in the compacted object, so this must be survivable rather than fatal.
    [Fact]
    public async Task An_object_that_disappears_between_listing_and_reading_is_skipped()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a");
        var b = NewDevice(bucket, "b");

        await a.Engine.RecordAsync(Op(a, "title", "\"gone\""));
        await a.Engine.SyncAsync();
        foreach (var key in bucket.Keys.Where(k => k.StartsWith("clients/a/", StringComparison.Ordinal)).ToList())
        {
            bucket.RemoveDirectly(key);
        }

        await b.Engine.SyncAsync();

        Assert.Equal(SyncPhase.Idle, b.Engine.Status.Phase);
    }

    // Pulling a peer's operation must advance this device's clock past it, or an edit made in response
    // could carry an earlier stamp and lose to the thing it was responding to.
    [Fact]
    public async Task Pulling_advances_the_clock_so_a_reply_wins()
    {
        var bucket = new FakeObjectStore();
        var a = NewDevice(bucket, "a", startMs: 0);
        var b = NewDevice(bucket, "b", startMs: 10_000_000);

        await b.Engine.RecordAsync(Op(b, "title", "\"from the future\""));
        await b.Engine.SyncAsync();
        await a.Engine.SyncAsync();

        // A's wall clock is far behind B's, so without observing B's stamp this reply would sort earlier.
        await a.Engine.RecordAsync(Op(a, "title", "\"my reply\""));
        await a.Engine.SyncAsync();
        await b.Engine.SyncAsync();

        Assert.Equal("\"my reply\"", b.State.Get("Todo", Row)!.Values["title"]);
    }

    [Fact]
    public void A_client_id_containing_a_slash_is_rejected()
    {
        var bucket = new FakeObjectStore();

        Assert.Throws<ArgumentException>(() => new SyncEngine(
            bucket, new InMemorySyncStore(), new HybridLogicalClock("a/b"), new SyncState()));
    }

    [Fact]
    public void A_root_prefix_without_a_trailing_slash_is_rejected()
    {
        var bucket = new FakeObjectStore();

        Assert.Throws<ArgumentException>(() => new SyncEngine(
            bucket, new InMemorySyncStore(), new HybridLogicalClock("a"), new SyncState(),
            new SyncEngineOptions { RootPrefix = "clients" }));
    }

    private static SyncOp Op(Device device, string field, string value) =>
        SyncOp.SetFields("Todo", Row, device.Clock.Tick(),
            new Dictionary<string, string> { [field] = value });

    private sealed class Device
    {
        public Device(FakeObjectStore bucket, string id, long startMs = 0, InMemorySyncStore? store = null)
        {
            Clock = new HybridLogicalClock(id, new FixedTime(Base.AddMilliseconds(startMs)));
            State = new SyncState();
            Engine = new SyncEngine(bucket, store ?? new InMemorySyncStore(), Clock, State,
                new SyncEngineOptions { TimeProvider = new FixedTime(Base) });
        }

        public HybridLogicalClock Clock { get; }

        public SyncState State { get; }

        public SyncEngine Engine { get; }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
