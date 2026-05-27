using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Server;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public class LiveSessionStoreTests
{
    private static LiveSessionStore NewStore(IHostApplicationLifetime? lifetime = null) =>
        new(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            lifetime);

    [Fact]
    public void Create_AssignsUniqueIds()
    {
        var store = NewStore();
        var s1 = store.Create(_ => new StubComponent(Span()));
        var s2 = store.Create(_ => new StubComponent(Span()));
        Assert.NotEqual(s1.Id, s2.Id);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void Get_ReturnsRegisteredSession()
    {
        var store = NewStore();
        var view = new StubComponent(Span());
        var session = store.Create(_ => view);

        var fetched = store.Get(session.Id);

        Assert.NotNull(fetched);
        Assert.Same(view, fetched!.View);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var store = NewStore();
        Assert.Null(store.Get("nope"));
    }

    [Fact]
    public void Remove_DropsSession()
    {
        var store = NewStore();
        var session = store.Create(_ => new StubComponent(Span()));

        store.Remove(session.Id);

        Assert.Null(store.Get(session.Id));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task ScheduleRemoval_AfterDelay_RemovesSession()
    {
        var store = NewStore();
        var session = store.Create(_ => new StubComponent(Span()));
        var id = session.Id;

        store.ScheduleRemoval(id, TimeSpan.FromMilliseconds(50));

        await WaitForAsync(() => store.Count == 0, TimeSpan.FromSeconds(2));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task ScheduleRemoval_ThenGet_CancelsRemoval_SessionStaysRegistered()
    {
        var store = NewStore();
        var session = store.Create(_ => new StubComponent(Span()));

        store.ScheduleRemoval(session.Id, TimeSpan.FromMilliseconds(200));
        var fetched = store.Get(session.Id);

        Assert.NotNull(fetched);
        await Task.Delay(400);
        Assert.NotNull(store.Get(session.Id));
    }

    [Fact]
    public async Task ScheduleRemoval_TwiceForSameId_ReplacesEarlierSchedule()
    {
        var store = NewStore();
        var session = store.Create(_ => new StubComponent(Span()));

        store.ScheduleRemoval(session.Id, TimeSpan.FromMilliseconds(50));
        store.ScheduleRemoval(session.Id, TimeSpan.FromSeconds(5));

        await Task.Delay(200);
        Assert.NotNull(store.Get(session.Id));
    }

    [Fact]
    public void ScheduleRemoval_UnknownId_NoOp()
    {
        var store = NewStore();

        store.ScheduleRemoval("missing", TimeSpan.FromMilliseconds(50));

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task ScheduleRemoval_StoppingTokenAlreadyCancelled_RemovesImmediately()
    {
        var lifetime = new FakeLifetime();
        var store = NewStore(lifetime);
        var session = store.Create(_ => new StubComponent(Span()));
        lifetime.StopApplication();

        store.ScheduleRemoval(session.Id, TimeSpan.FromSeconds(30));

        await WaitForAsync(() => store.Count == 0, TimeSpan.FromSeconds(2));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task RemoveAsync_DisposesSession()
    {
        var store = NewStore();
        var disposed = new TaskCompletionSource();
        var session = store.Create(_ => new AsyncDisposableTracker(disposed));

        await store.RemoveAsync(session.Id);

        Assert.Null(store.Get(session.Id));
        Assert.True(disposed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RerenderAllAsync_NoSessions_ReturnsCompletedTask()
    {
        var store = NewStore();

        await store.RerenderAllAsync();
    }

    [Fact]
    public async Task RerenderAllAsync_WithSessions_CompletesWithoutThrowing()
    {
        var store = NewStore();
        store.Create(_ => new StubComponent(Span()));
        store.Create(_ => new StubComponent(Span()));

        var task = store.RerenderAllAsync();
        await task;

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public async Task DisposeAsync_DisposesEachSessionAndCancelsPending()
    {
        var store = NewStore();
        var disposed = new TaskCompletionSource();
        var session = store.Create(_ => new AsyncDisposableTracker(disposed));
        store.ScheduleRemoval(session.Id, TimeSpan.FromSeconds(30));

        await store.DisposeAsync();

        Assert.Equal(0, store.Count);
        Assert.True(disposed.Task.IsCompletedSuccessfully);
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

        protected override RenderResult Render() => Span();
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
