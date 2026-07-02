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
    public Task<int> Handle(Add query, CancellationToken cancellationToken) =>
        Task.FromResult(query.A + query.B);
}

// ---- Void command ----
public sealed record Poke(string Name) : ICommand;

public sealed class PokeHandler(Recorder recorder) : ICommandHandler<Poke>
{
    public Task Handle(Poke command, CancellationToken cancellationToken)
    {
        recorder.Add($"poke:{command.Name}");
        return Task.CompletedTask;
    }
}

// ---- Command with result ----
public sealed record CreateThing(string Name) : ICommand<int>;

public sealed class CreateThingHandler : ICommandHandler<CreateThing, int>
{
    public Task<int> Handle(CreateThing command, CancellationToken cancellationToken) =>
        Task.FromResult(command.Name.Length);
}

// ---- Notification with two handlers ----
public sealed record Pinged(string Message) : INotification;

public sealed class PingedHandlerA(Recorder recorder) : INotificationHandler<Pinged>
{
    public Task Handle(Pinged notification, CancellationToken cancellationToken)
    {
        recorder.Add($"A:{notification.Message}");
        return Task.CompletedTask;
    }
}

public sealed class PingedHandlerB(Recorder recorder) : INotificationHandler<Pinged>
{
    public Task Handle(Pinged notification, CancellationToken cancellationToken)
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
    public async Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
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
    public async Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
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
    public Task<int> Handle(Add request, RequestHandlerDelegate<int> next, CancellationToken cancellationToken)
    {
        recorder.Add("short-circuit");
        return Task.FromResult(999);
    }
}

// ---- A request with no registered handler (to prove the clear runtime error) ----
public sealed record Orphan : IQuery<int>;
