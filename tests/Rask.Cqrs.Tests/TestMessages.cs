using System.Collections.Concurrent;

namespace Rask.Cqrs.Tests;

// A shared, per-instance sink so tests can assert what ran and in what order without static state.
public sealed class Recorder
{
    public ConcurrentQueue<string> Log { get; } = new();

    public void Add(string entry) => Log.Enqueue(entry);

    public IReadOnlyList<string> Entries => Log.ToArray();
}

// ---- Query ----
public sealed record Add(int A, int B) : IQuery<int>;

public sealed class AddHandler : IQueryHandler<Add, int>
{
    public Task<int> HandleAsync(Add query, CancellationToken cancellationToken) =>
        Task.FromResult(query.A + query.B);
}

// ---- Void command ----
public sealed record Poke(string Name) : ICommand;

public sealed class PokeHandler(Recorder recorder) : ICommandHandler<Poke>
{
    public Task HandleAsync(Poke command, CancellationToken cancellationToken)
    {
        recorder.Add($"poke:{command.Name}");
        return Task.CompletedTask;
    }
}

// ---- Command with result ----
public sealed record CreateThing(string Name) : ICommand<int>;

public sealed class CreateThingHandler : ICommandHandler<CreateThing, int>
{
    public Task<int> HandleAsync(CreateThing command, CancellationToken cancellationToken) =>
        Task.FromResult(command.Name.Length);
}

// ---- Notification with two handlers ----
public sealed record Pinged(string Message) : INotification;

public sealed class PingedHandlerA(Recorder recorder) : INotificationHandler<Pinged>
{
    public Task HandleAsync(Pinged notification, CancellationToken cancellationToken)
    {
        recorder.Add($"A:{notification.Message}");
        return Task.CompletedTask;
    }
}

public sealed class PingedHandlerB(Recorder recorder) : INotificationHandler<Pinged>
{
    public Task HandleAsync(Pinged notification, CancellationToken cancellationToken)
    {
        recorder.Add($"B:{notification.Message}");
        return Task.CompletedTask;
    }
}

// ---- Notification nobody handles ----
public sealed record Unheard : INotification;

// ---- An open-generic behavior that records entry/exit around every request ----
public sealed class TracingBehavior<TRequest, TResult>(Recorder recorder) : IPipelineBehavior<TRequest, TResult>
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        recorder.Add($"trace-in:{typeof(TRequest).Name}");
        var result = await next();
        recorder.Add($"trace-out:{typeof(TRequest).Name}");
        return result;
    }
}

// ---- A second behavior to prove ordering ----
public sealed class SecondBehavior<TRequest, TResult>(Recorder recorder) : IPipelineBehavior<TRequest, TResult>
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        recorder.Add("second-in");
        var result = await next();
        recorder.Add("second-out");
        return result;
    }
}

// ---- A short-circuiting closed behavior for Add: returns 999 without calling next ----
public sealed class ShortCircuitAdd(Recorder recorder) : IPipelineBehavior<Add, int>
{
    public Task<int> HandleAsync(Add request, RequestHandlerDelegate<int> next, CancellationToken cancellationToken)
    {
        recorder.Add("short-circuit");
        return Task.FromResult(999);
    }
}

// ---- A request with no registered handler (to prove the clear runtime error) ----
public sealed record Orphan : IQuery<int>;

// ---- A notification whose handlers fail, for the publish failure-mode tests. One succeeds and two
// throw; the throwing handlers are async (record, yield, then throw) so under WhenAll every handler's
// task is started before any of them faults. ----
public sealed record Grumble(string Tag) : INotification;

public sealed class GrumbleOk(Recorder recorder) : INotificationHandler<Grumble>
{
    public Task HandleAsync(Grumble notification, CancellationToken cancellationToken)
    {
        recorder.Add($"ok:{notification.Tag}");
        return Task.CompletedTask;
    }
}

public sealed class GrumbleBoomOne(Recorder recorder) : INotificationHandler<Grumble>
{
    public async Task HandleAsync(Grumble notification, CancellationToken cancellationToken)
    {
        recorder.Add($"boom1:{notification.Tag}");
        await Task.Yield();
        throw new InvalidOperationException("boom-1");
    }
}

public sealed class GrumbleBoomTwo(Recorder recorder) : INotificationHandler<Grumble>
{
    public async Task HandleAsync(Grumble notification, CancellationToken cancellationToken)
    {
        recorder.Add($"boom2:{notification.Tag}");
        await Task.Yield();
        throw new InvalidOperationException("boom-2");
    }
}
