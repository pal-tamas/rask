using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;
using Rask.Data;

namespace Rask.Background.Tests;

// ── The jobs the work-notice tests drive (#1293): each reports how far along it is, parks until the test lets it go, then
//    ends done — or, BurstFailJob, failed. ─────────────────────────────────────────────────────────────────────────────────

/// <summary>Reports <c>1..Burst</c> of <c>Of</c> back to back, then parks until released.</summary>
public sealed record BurstJob(string Tag, int Burst, int Of) : IJob;

public sealed class BurstJobHandler(LaneGate lanes) : ICommandHandler<BurstJob>
{
    public async Task Handle(BurstJob command)
    {
        for (var done = 1; done <= command.Burst; done++)
        {
            Jobs.Report(done, command.Of, $"{done} of {command.Of} rows");
        }

        lanes.Enter(command.Tag);
        await lanes.Released(command.Tag).WaitAsync(Current.Cancellation).ConfigureAwait(false);
    }
}

/// <summary>Reports once, then throws — on its last attempt, a failure for good.</summary>
public sealed record BurstFailJob(string Tag) : IJob;

public sealed class BurstFailJobHandler : ICommandHandler<BurstFailJob>
{
    public Task Handle(BurstFailJob command)
    {
        Jobs.Report(1, 4);
        throw new InvalidOperationException("secret connection string in the message");
    }
}

/// <summary>Every <see cref="WorkReported" /> a subscription heard, in order — opened as the user and tenant it is given.</summary>
public sealed class HeardWork : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<WorkReported> _heard = new();
    private readonly Task _listening;

    private HeardWork(IServiceProvider services, ISubscription<WorkReported>? watch, Guid? user, Guid? tenant)
    {
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Opened = listening.Task;
        _listening = Task.Run(async () =>
        {
            using var who = Current.UseUser(user);
            using var where = tenant is { } t ? Tenant.Use(t) : null;
            var dispatcher = services.GetRequiredService<IDispatcher>();
            var events = watch is null ? dispatcher.Subscribe<WorkReported>(_stop.Token) : dispatcher.Subscribe(watch, _stop.Token);
            try
            {
                await using var reading = events.GetAsyncEnumerator(_stop.Token);
                var next = reading.MoveNextAsync();
                listening.TrySetResult();
                while (await next)
                {
                    _heard.Enqueue(reading.Current);
                    next = reading.MoveNextAsync();
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // Closed by the test.
            }
            catch (Exception ex)
            {
                Refused = ex;
                listening.TrySetResult();
            }
        });
    }

    /// <summary>Completes once the subscription is open (or refused).</summary>
    public Task Opened { get; }

    /// <summary>Why it could not open, if it could not.</summary>
    public Exception? Refused { get; private set; }

    public IReadOnlyList<WorkReported> Heard => [.. _heard];

    /// <summary>Every <see cref="WorkReported" /> of every job — the type watched, as server code may.</summary>
    public static Task<HeardWork> Everything(JobsHarness h) => Open(h, watch: null, user: null, tenant: null);

    /// <summary>The work of <paramref name="user" /> in <paramref name="tenant" />, through the subscription the kit opens.</summary>
    public static Task<HeardWork> Own(JobsHarness h, Guid? user, Guid? tenant = null, string? lane = null) =>
        Open(h, new WatchOwnWork(lane), user, tenant);

    private static async Task<HeardWork> Open(JobsHarness h, ISubscription<WorkReported>? watch, Guid? user, Guid? tenant)
    {
        var feed = h.Get<EventFeed>();
        var before = feed.ListenerCount(typeof(WorkReported));
        var heard = new HeardWork(h.Get<IServiceProvider>(), watch, user, tenant);
        await heard.Opened.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling);
        await h.WaitUntilAsync(() => Task.FromResult(heard.Refused is not null || feed.ListenerCount(typeof(WorkReported)) > before));
        return heard;
    }

    /// <summary>Waits until something heard matches <paramref name="what" />.</summary>
    public Task Hears(JobsHarness h, Func<WorkReported, bool> what) =>
        h.WaitUntilAsync(() => Task.FromResult(Heard.Any(what)), expression: "heard the work asked for");

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _listening;
        _stop.Dispose();
    }
}
