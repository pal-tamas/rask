using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Server;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class LiveSessionStoreTests : global::Rask.Core.RaskMarkup
{
    private static LiveSessionStore NewStore(IHostApplicationLifetime? lifetime = null) =>
        new(
            RenderHarness.EmptyServices().GetRequiredService<IServiceScopeFactory>(),
            lifetime);

    [Fact]
    public void Creating_sessions_assigns_unique_ids()
    {
        var store = NewStore();
        var s1 = store.Create(_ => new StubComponent(Span));
        var s2 = store.Create(_ => new StubComponent(Span));

        Assert.NotEqual(s1.Id, s2.Id);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void Getting_a_registered_session_finds_it()
    {
        var store = NewStore();
        var view = new StubComponent(Span);
        var session = store.Create(_ => view);

        var fetched = store.Get(session.Id);

        Assert.NotNull(fetched);
        Assert.Same(view, fetched!.View);
    }

    [Fact]
    public void Getting_an_unknown_id_gives_null()
    {
        var store = NewStore();

        Assert.Null(store.Get("nope"));
    }

    [Fact]
    public void Removing_a_session_drops_it()
    {
        var store = NewStore();
        var session = store.Create(_ => new StubComponent(Span));

        store.Remove(session.Id);

        Assert.Null(store.Get(session.Id));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task A_scheduled_removal_removes_the_session_after_the_delay()
    {
        var store = NewStore();
        var session = store.Create(_ => new StubComponent(Span));
        var id = session.Id;

        store.ScheduleRemoval(id, TimeSpan.FromMilliseconds(50));

        await WaitForAsync(() => store.Count == 0, TimeSpan.FromSeconds(2));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task A_get_after_a_scheduled_removal_cancels_it_and_the_session_stays_registered()
    {
        var store = NewStore();
        var session = store.Create(_ => new StubComponent(Span));

        store.ScheduleRemoval(session.Id, TimeSpan.FromMilliseconds(200));
        var fetched = store.Get(session.Id);

        Assert.NotNull(fetched);
        await Task.Delay(400);
        Assert.NotNull(store.Get(session.Id));
    }

    [Fact]
    public async Task Scheduling_removal_twice_for_the_same_id_replaces_the_earlier_schedule()
    {
        var store = NewStore();
        var session = store.Create(_ => new StubComponent(Span));

        store.ScheduleRemoval(session.Id, TimeSpan.FromMilliseconds(50));
        store.ScheduleRemoval(session.Id, TimeSpan.FromSeconds(5));

        await Task.Delay(200);
        Assert.NotNull(store.Get(session.Id));
    }

    [Fact]
    public void Scheduling_removal_of_an_unknown_id_does_nothing()
    {
        var store = NewStore();

        store.ScheduleRemoval("missing", TimeSpan.FromMilliseconds(50));

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task Scheduling_removal_with_an_already_cancelled_stopping_token_removes_immediately()
    {
        var lifetime = new FakeLifetime();
        var store = NewStore(lifetime);
        var session = store.Create(_ => new StubComponent(Span));
        lifetime.StopApplication();

        store.ScheduleRemoval(session.Id, TimeSpan.FromSeconds(30));

        await WaitForAsync(() => store.Count == 0, TimeSpan.FromSeconds(2));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task Removing_a_session_asynchronously_disposes_it()
    {
        var store = NewStore();
        var disposed = new TaskCompletionSource();
        var session = store.Create(_ => new AsyncDisposableTracker(disposed));

        await store.RemoveAsync(session.Id);

        Assert.Null(store.Get(session.Id));
        Assert.True(disposed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Rerendering_all_with_no_sessions_gives_a_completed_task()
    {
        var store = NewStore();

        await store.RerenderAllAsync();
    }

    [Fact]
    public async Task Rerendering_all_with_sessions_completes_without_throwing()
    {
        var store = NewStore();
        store.Create(_ => new StubComponent(Span));
        store.Create(_ => new StubComponent(Span));

        var task = store.RerenderAllAsync();
        await task;

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public async Task Disposing_the_store_disposes_each_session_and_cancels_pending_removals()
    {
        var store = NewStore();
        var disposed = new TaskCompletionSource();
        var session = store.Create(_ => new AsyncDisposableTracker(disposed));
        store.ScheduleRemoval(session.Id, TimeSpan.FromSeconds(30));

        await store.DisposeAsync();

        Assert.Equal(0, store.Count);
        Assert.True(disposed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void An_uncapped_store_always_creates_a_session()
    {
        var store = NewStore(); // MaxSessions defaults to 0 (unlimited)

        for (var i = 0; i < 5; i++)
        {
            Assert.NotNull(store.TryCreate(_ => new StubComponent(Span)));
        }

        Assert.Equal(5, store.Count);
    }

    [Fact]
    public void Over_the_cap_creation_gives_null_and_mints_no_session()
    {
        var store = NewStore();
        store.MaxSessions = 2;

        Assert.NotNull(store.TryCreate(_ => new StubComponent(Span)));
        Assert.NotNull(store.TryCreate(_ => new StubComponent(Span)));

        // Third reservation exceeds the cap: rejected, and no session is built/stored.
        Assert.Null(store.TryCreate(_ => new StubComponent(Span)));
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void A_removal_frees_a_cap_slot_for_creation()
    {
        var store = NewStore();
        store.MaxSessions = 1;

        var first = store.TryCreate(_ => new StubComponent(Span));
        Assert.NotNull(first);
        Assert.Null(store.TryCreate(_ => new StubComponent(Span))); // at cap

        store.Remove(first!.Id); // releases the reservation

        Assert.NotNull(store.TryCreate(_ => new StubComponent(Span)));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task A_concurrent_burst_of_creations_never_exceeds_the_cap()
    {
        var store = NewStore();
        store.MaxSessions = 10;

        // 100 concurrent reservations against a cap of 10 — the atomic reservation must admit
        // exactly 10 and reject the rest, with no torn count from the race.
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => store.TryCreate(_ => new StubComponent(Span)) is not null))
            .ToArray();
        var admitted = await Task.WhenAll(tasks);

        Assert.Equal(10, admitted.Count(ok => ok));
        Assert.Equal(10, store.Count);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    private sealed class AsyncDisposableTracker : Component, IAsyncDisposable
    {
        private readonly TaskCompletionSource _disposed;

        public AsyncDisposableTracker(TaskCompletionSource disposed) => _disposed = disposed;

        public ValueTask DisposeAsync()
        {
            _disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        protected override Component? Render() => Span;
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopped = new();
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            _stopping.Cancel();
            _stopped.Cancel();
        }
    }
}
