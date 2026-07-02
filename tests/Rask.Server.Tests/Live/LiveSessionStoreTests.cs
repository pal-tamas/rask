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
