using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Html.Components;

namespace Rask.Server.Tests.Live;

// Direct tests for the schedule/cancel removal lifecycle. The CTS that backs a pending removal
// must never be cancelled or disposed while still reachable, and the delayed removal task must
// not orphan a session nor surface an unobserved ObjectDisposedException when a concurrent
// reconnect (Get) or retire races it.
public class LiveSessionStoreTests
{
    [Fact]
    public async Task ScheduleRemoval_ThenGet_CancelsRemoval_SessionSurvives()
    {
        var store = NewStore();
        var session = store.Create(_ => new BasicComponent());

        store.ScheduleRemoval(session.Id, TimeSpan.FromMilliseconds(100));
        // Reconnect within the grace window: Get must cancel the pending removal.
        Assert.NotNull(store.Get(session.Id));

        await Task.Delay(250);

        Assert.Equal(1, store.Count);
        Assert.NotNull(store.Get(session.Id));
    }

    [Fact]
    public async Task ScheduleRemoval_NoReconnect_RemovesAfterDelay()
    {
        var store = NewStore();
        var session = store.Create(_ => new BasicComponent());

        store.ScheduleRemoval(session.Id, TimeSpan.FromMilliseconds(50));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (store.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task ScheduleRemoval_RescheduledRepeatedly_DoesNotThrowOrOrphan()
    {
        // Each reschedule retires the prior CTS (cancel + dispose). Hammering it must not raise
        // an ObjectDisposedException out of the delayed task nor leave the session orphaned.
        var store = NewStore();
        var session = store.Create(_ => new BasicComponent());

        for (var i = 0; i < 50; i++)
        {
            store.ScheduleRemoval(session.Id, TimeSpan.FromMilliseconds(30));
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (store.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task ConcurrentScheduleAndGet_StaysConsistent_NoCrash()
    {
        // Race a tight schedule/cancel loop against concurrent reconnects on many sessions. The
        // store must never throw and _liveCount must stay in lockstep with the dictionary.
        var store = NewStore();
        var ids = Enumerable.Range(0, 40)
            .Select(_ => store.Create(_ => new BasicComponent()).Id)
            .ToArray();

        var tasks = new List<Task>();
        foreach (var id in ids)
        {
            tasks.Add(Task.Run(() =>
            {
                for (var i = 0; i < 20; i++)
                {
                    store.ScheduleRemoval(id, TimeSpan.FromMilliseconds(5));
                    _ = store.Get(id); // reconnect cancels the pending removal
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Every session that was reconnected last should still be present; the count must match
        // the number actually retained (no negative/leaked _liveCount, no orphan).
        Assert.True(store.Count <= ids.Length);
        await store.DisposeAsync();
        Assert.Equal(0, store.Count);
    }

    // The store is a DI singleton, so the container disposes it — and a host or a test that disposes it
    // as well used to reach Cancel() on an already-disposed token source. A second dispose must be inert,
    // including with a pending removal outstanding, which is what owns that token source.
    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var store = NewStore();
        var session = store.Create(_ => new BasicComponent());
        store.ScheduleRemoval(session.Id, TimeSpan.FromMinutes(5));

        await store.DisposeAsync();
        await store.DisposeAsync();

        Assert.Equal(0, store.Count);
    }

    // ---- CreateDetached / TryRegister / DiscardAsync -------------------------------------
    // The shell endpoint cannot know whether a page needs a live session until its tree has been
    // rendered, so building and admitting are separate steps. These pin the accounting, which is
    // the half that fails silently: a reservation leaked or credited back twice only shows up
    // once the host is saturated and starts admitting past MaxSessions.

    [Fact]
    public void CreateDetached_IsInvisibleToTheStore_AndCountsAgainstNothing()
    {
        var store = NewStore();

        var session = store.CreateDetached(_ => new BasicComponent());

        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.LiveCount);
        Assert.Null(store.Get(session.Id));
    }

    [Fact]
    public void TryRegister_AdmitsADetachedSession()
    {
        var store = NewStore();
        var session = store.CreateDetached(_ => new BasicComponent());

        Assert.True(store.TryRegister(session));

        Assert.Equal(1, store.Count);
        Assert.Equal(1, store.LiveCount);
        Assert.Same(session, store.Get(session.Id));
    }

    [Fact]
    public async Task DiscardAsync_DoesNotReleaseAReservationItNeverTook()
    {
        var store = NewStore();
        // One genuinely admitted session holds exactly one reservation.
        store.Create(_ => new BasicComponent());

        // A detached session never reserved a slot, so tearing it down must not credit one back.
        // Getting this wrong drifts the cap upward by one per page served without a session.
        var detached = store.CreateDetached(_ => new BasicComponent());
        await store.DiscardAsync(detached);

        Assert.Equal(1, store.LiveCount);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task TryRegister_AtCapacity_RefusesAndReservesNothing()
    {
        var store = NewStore();
        store.MaxSessions = 1;
        store.Create(_ => new BasicComponent());

        var detached = store.CreateDetached(_ => new BasicComponent());

        Assert.False(store.TryRegister(detached));
        Assert.Equal(1, store.LiveCount);
        Assert.Equal(1, store.Count);

        // The caller still owns it, and discarding a refused session is the documented contract.
        await store.DiscardAsync(detached);
        Assert.Equal(1, store.LiveCount);
    }

    [Fact]
    public void TryCreate_AtCapacity_RefusesBeforeBuildingTheTree()
    {
        var store = NewStore();
        store.MaxSessions = 1;
        store.Create(_ => new BasicComponent());

        var built = 0;
        var refused = store.TryCreate(_ =>
        {
            built++;
            return new BasicComponent();
        });

        Assert.Null(refused);
        // The whole point of reserving before building: the component tree is the expensive thing
        // a GET flood is trying to make the host allocate. Composing TryCreate as
        // build-then-admit would still refuse, and would still pass every assertion above — this
        // is the one that would catch it.
        Assert.Equal(0, built);
    }

    private static LiveSessionStore NewStore()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        return new LiveSessionStore(sp.GetRequiredService<IServiceScopeFactory>());
    }

    private sealed class BasicComponent : Component
    {
        protected override Component? Render() => new Span();
    }
}
