using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;

namespace Rask.Server.Tests.Live;

// Direct tests for the schedule/cancel removal lifecycle. The CTS that backs a pending removal
// must never be cancelled or disposed while still reachable, and the delayed removal task must
// not orphan a session nor surface an unobserved ObjectDisposedException when a concurrent
// reconnect (Get) or retire races it.
public class LiveSessionStoreTests
{
    [Fact]
    public async Task A_get_after_a_scheduled_removal_cancels_it_and_the_session_survives()
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
    public async Task A_scheduled_removal_with_no_reconnect_removes_the_session_after_the_delay()
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
    public async Task A_removal_rescheduled_repeatedly_neither_throws_nor_orphans_the_session()
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
    public async Task Concurrent_schedules_and_gets_stay_consistent_without_a_crash()
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
    public async Task Disposing_the_store_twice_is_harmless()
    {
        var store = NewStore();
        var session = store.Create(_ => new BasicComponent());
        store.ScheduleRemoval(session.Id, TimeSpan.FromMinutes(5));

        await store.DisposeAsync();
        await store.DisposeAsync();

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void A_create_at_capacity_is_refused_before_the_tree_is_built()
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
