using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class BackgroundSyncTests
{
    [Fact]
    public async Task IsSupported_AsksForBothManagersSeparately()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSync.supported", true);
        js.SetResponse("__raskSync.periodicSupported", false);
        var sync = new BackgroundSync(js);

        // One-shot ships years ahead of periodic in every engine that has either, so a caller has to be
        // able to ask about them independently rather than getting one "background sync" answer.
        Assert.True(await sync.IsSupportedAsync());
        Assert.False(await sync.IsPeriodicSupportedAsync());
    }

    [Fact]
    public async Task RequestSync_PassesTheTagAndReportsWhetherTheBrowserTookIt()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSync.request", true);

        Assert.True(await new BackgroundSync(js).RequestSyncAsync("flush-drafts"));
        Assert.Equal(["flush-drafts"], js.ArgsFor("__raskSync.request"));
    }

    [Fact]
    public async Task RequestSync_IsFalseWhenTheBrowserOrTheServiceWorkerCannotTakeIt()
    {
        // No canned response → the helper answers false, which is how "no SW registered", "not supported"
        // and "the browser refused" all surface. None of them is an exception at the call site.
        Assert.False(await new BackgroundSync(new FakeJsRuntime()).RequestSyncAsync("flush-drafts"));
    }

    [Fact]
    public async Task RequestPeriodicSync_SendsTheIntervalInMilliseconds()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSync.requestPeriodic", true);

        Assert.True(await new BackgroundSync(js).RequestPeriodicSyncAsync("refresh", TimeSpan.FromHours(12)));

        var args = js.ArgsFor("__raskSync.requestPeriodic");
        Assert.Equal("refresh", args![0]);
        // minInterval is milliseconds on the wire; a TimeSpan that arrived as ticks or seconds would
        // silently ask for an interval four orders of magnitude off.
        Assert.Equal(43_200_000d, args[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RequestSync_RejectsATagThatNamesNothing(string? tag)
    {
        var sync = new BackgroundSync(new FakeJsRuntime());

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await sync.RequestSyncAsync(tag!));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await sync.RequestPeriodicSyncAsync(tag!, TimeSpan.FromHours(1)));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await sync.UnregisterPeriodicAsync(tag!));
    }

    [Fact]
    public async Task RequestPeriodicSync_RejectsANonPositiveInterval()
    {
        var sync = new BackgroundSync(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await sync.RequestPeriodicSyncAsync("refresh", TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await sync.RequestPeriodicSyncAsync("refresh", TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task Tags_AreEmptyRatherThanNullWhenTheApiIsAbsent()
    {
        // The FakeJsRuntime hands back default(string[]) — null — exactly as an absent helper would. A null
        // list here would turn a plain "not supported" into a NullReferenceException inside a foreach.
        var sync = new BackgroundSync(new FakeJsRuntime());

        Assert.Empty(await sync.GetPendingTagsAsync());
        Assert.Empty(await sync.GetPeriodicTagsAsync());
    }

    [Fact]
    public async Task Tags_ComeBackFromTheHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSync.tags", new[] { "flush-drafts", "upload-photos" });
        js.SetResponse("__raskSync.periodicTags", new[] { "refresh" });
        var sync = new BackgroundSync(js);

        Assert.Equal(["flush-drafts", "upload-photos"], await sync.GetPendingTagsAsync());
        Assert.Equal(["refresh"], await sync.GetPeriodicTagsAsync());
    }

    [Fact]
    public async Task PeriodicPermission_IsReadNotRequested()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSync.periodicPermission", "granted");

        Assert.Equal("granted", await new BackgroundSync(js).GetPeriodicPermissionAsync());
        // There is no prompt for periodic-background-sync — the browser decides. Asserting the helper is
        // only ever queried keeps a future "request" path from being bolted on where none can exist.
        Assert.Equal(1, js.CallCount("__raskSync.periodicPermission"));
    }

    [Fact]
    public async Task Subscribing_ArmsTheHelperSoAnythingBufferedDuringBootIsReleased()
    {
        var js = new FakeJsRuntime();

        await using var _ = await new BackgroundSync(js).OnSyncAsync(_ => Task.CompletedTask);

        // listen() is what flushes syncs that landed before the runtime was ready. Without this call the
        // page boots, the sync is held forever, and nothing looks broken.
        Assert.Equal(1, js.CallCount("__raskSync.listen"));
    }

    [Fact]
    public async Task AFiredSync_ReachesEverySubscriber_WithTheTagAndKind()
    {
        var js = new FakeJsRuntime();
        var sync = new BackgroundSync(js);
        var first = new List<BackgroundSyncEvent>();
        var second = new List<BackgroundSyncEvent>();

        // One tag can legitimately interest several components — a draft queue and a badge count — so this
        // fans out, unlike the id-keyed device wrappers where an event belongs to exactly one watch.
        await using var a = await sync.OnSyncAsync(e =>
        {
            first.Add(e);
            return Task.CompletedTask;
        });
        await using var b = await sync.OnSyncAsync(e =>
        {
            second.Add(e);
            return Task.CompletedTask;
        });

        await BackgroundSyncInterop.Fired(false, "flush-drafts");
        await BackgroundSyncInterop.Fired(true, "refresh");

        Assert.Equal([new BackgroundSyncEvent("flush-drafts", false), new BackgroundSyncEvent("refresh", true)], first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task DisposingASubscription_StopsThatHandlerOnly()
    {
        var js = new FakeJsRuntime();
        var sync = new BackgroundSync(js);
        var kept = new List<string>();
        var dropped = new List<string>();

        await using var stays = await sync.OnSyncAsync(e =>
        {
            kept.Add(e.Tag);
            return Task.CompletedTask;
        });
        var goes = await sync.OnSyncAsync(e =>
        {
            dropped.Add(e.Tag);
            return Task.CompletedTask;
        });

        await goes.DisposeAsync();
        await goes.DisposeAsync();   // idempotent: a second dispose must not unregister someone else's id
        await BackgroundSyncInterop.Fired(false, "flush-drafts");

        Assert.Equal(["flush-drafts"], kept);
        Assert.Empty(dropped);
    }

    [Fact]
    public async Task AFiredSyncWithNoSubscribers_IsHarmless()
    {
        // The service worker forwards to whatever clients exist; a page that never subscribed is a normal
        // outcome, not an error.
        await BackgroundSyncInterop.Fired(false, "flush-drafts");
    }
}
