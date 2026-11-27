using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;
using Rask.Data;

namespace Rask.Cqrs.Transport.Tests;

// The bell of a browser half (#1348), over real HTTP: its board asks the server for the signed-in user's rows, and its
// watch opens on the server's event stream, admitted by the same OwnWorkPolicy a server page's is.
public sealed class OwnWorkTransportTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly Guid Alice = Guid.Parse("0f8b6c2a-1d2e-4f30-9a4b-5c6d7e8f9a0b");
    private static readonly Guid Bob = Guid.Parse("7a6b5c4d-3e2f-4a1b-8c9d-0e1f2a3b4c5d");
    private static readonly DateTime At = new(2026, 11, 17, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_browser_reads_the_rows_of_whoever_is_signed_in_and_how_often_they_change()
    {
        await using var wire = Wire.Connect(user: Alice.ToString(), serverServices: Server);
        var board = new RemoteWorkBoard(wire.Transport);

        var rows = await board.Mine("imports", TestContext.Current.CancellationToken);

        var row = Assert.Single(rows);
        Assert.Equal(new WorkReported(Id(7), "Import of prices.csv", "1,200 rows kept", 1200, 5000, WorkState.Running, At), row);
        Assert.Null(row.Owner);
        Assert.Equal(TimeSpan.FromSeconds(3), board.Every);
        Assert.Equal(HttpMethod.Get, wire.Recorder.Last.Method);
    }

    [Fact]
    public async Task Nobody_signed_in_reads_an_empty_board_rather_than_a_failure()
    {
        await using var wire = Wire.Connect(user: null, serverServices: Server);
        var board = new RemoteWorkBoard(wire.Transport);

        var rows = await board.Mine(null, TestContext.Current.CancellationToken);

        Assert.Empty(rows);
        Assert.Equal(RemoteWorkBoard.Unread, board.Every);
    }

    [Fact]
    public async Task A_browser_s_watch_hears_its_own_user_s_work_and_nobody_else_s()
    {
        await using var wire = Wire.Connect(user: Alice.ToString(), serverServices: Server);
        await using var watch = await OpenAsync(wire);

        await wire.Server.Publish(Work(Bob, Id(8), WorkState.Running), TestContext.Current.CancellationToken);
        await wire.Server.Publish(Work(Alice, Id(7), WorkState.Done), TestContext.Current.CancellationToken);

        var heard = await watch.NextAsync();
        Assert.Equal(Id(7), heard.Id);
        Assert.Equal(WorkState.Done, heard.State);
        Assert.Null(heard.Owner);
    }

    [Fact]
    public async Task Nobody_signed_in_may_open_the_watch()
    {
        await using var wire = Wire.Connect(user: null, serverServices: Server);

        var refused = await Assert.ThrowsAsync<RemoteDispatchException>(() => OpenAsync(wire));

        Assert.Equal(401, refused.StatusCode);
    }

    // The server half as AddRaskJobs leaves it: the board and the policy, reading who asks in the request's own scope.
    private static void Server(IServiceCollection services)
    {
        OwnWorkWire.Install();
        services.AddHttpContextAccessor();
        services.AddScoped<IPrincipalSource, RequestPrincipal>();
        services.AddScoped<IWorkBoard, PrincipalBoard>();
        services.AddScoped<IWatchPolicy<WatchOwnWork>>(static scope => new OwnWorkPolicy(scope));
    }

    private static WorkReported Work(Guid owner, Guid id, WorkState state) =>
        new(id, $"Job {id}", null, null, null, state, At) { Owner = owner };

    private static async Task<Watch> OpenAsync(Wire wire)
    {
        var stop = new CancellationTokenSource();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = wire.Subscriptions
            .Subscribe(OwnWorkWire.Watch, new WatchOwnWork(null), () => admitted.TrySetResult(), stop.Token)
            .GetAsyncEnumerator(stop.Token);
        var next = events.MoveNextAsync().AsTask();
        await Task.WhenAny(admitted.Task, next).WaitAsync(Wait);
        if (next.IsFaulted)
        {
            await stop.CancelAsync();
            stop.Dispose();
            await next;
        }

        return new Watch(events, next, stop);
    }

    private sealed class Watch(IAsyncEnumerator<IEvent> events, Task<bool> first, CancellationTokenSource stop) : IAsyncDisposable
    {
        public async Task<WorkReported> NextAsync()
        {
            Assert.True(await first.WaitAsync(Wait));
            return (WorkReported)events.Current;
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            await ((Task)first).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await events.DisposeAsync();
            stop.Dispose();
        }
    }

    // Who the request carries, as Rask.Server's principal source hands it to the data layer.
    private sealed class RequestPrincipal(IHttpContextAccessor http) : IPrincipalSource
    {
        public ClaimsPrincipal? Current => http.HttpContext?.User;
    }

    // Alice's import, for Alice: the board reads who asks from its own scope, as the jobs board does.
    private sealed class PrincipalBoard(IPrincipalSource principal) : IWorkBoard
    {
        public TimeSpan Every => TimeSpan.FromSeconds(3);

        public Task<IReadOnlyList<WorkReported>> Mine(string? lane, CancellationToken cancellationToken)
        {
            var who = principal.Current?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            IReadOnlyList<WorkReported> work = who == Alice.ToString() && lane == "imports"
                ? [new WorkReported(Id(7), "Import of prices.csv", "1,200 rows kept", 1200, 5000, WorkState.Running, At) { Owner = Alice }]
                : [];
            return Task.FromResult(work);
        }
    }

    // A job's id as a test names it: the nth, a version-7 Guid that reads as its number.
    private static Guid Id(int n) => new($"019ac4f0-5a00-7000-8000-{n:D12}");
}
