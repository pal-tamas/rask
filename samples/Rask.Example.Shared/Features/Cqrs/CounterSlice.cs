using Rask.Cqrs;

namespace Rask.Example.Shared.Features;

// A tiny vertical slice that shows all four Rask.Cqrs message shapes wired reflection-free by the
// source generator: a query, a command that returns a value, a notification the command publishes,
// and a pipeline behavior (decorator) that wraps every dispatch. The store is the slice's state.
public sealed class CqrsCounterStore
{
    private readonly Queue<string> _log = new();

    public int Count { get; private set; }

    public IReadOnlyList<string> Log => _log.ToArray();

    public int IncrementBy(int by)
    {
        Count += by;
        return Count;
    }

    public void Note(string entry)
    {
        _log.Enqueue(entry);
        while (_log.Count > 6)
        {
            _log.Dequeue();
        }
    }
}

public sealed record CounterState(int Count, IReadOnlyList<string> Log);

// --- Query: read the current count + recent pipeline log ---
public sealed record GetCounterState : IQuery<CounterState>;

public sealed class GetCounterStateHandler(CqrsCounterStore store) : IQueryHandler<GetCounterState, CounterState>
{
    public Task<CounterState> HandleAsync(GetCounterState query, CancellationToken cancellationToken) =>
        Task.FromResult(new CounterState(store.Count, store.Log));
}

// --- Command with a result: mutate, publish an event, return the new value ---
public sealed record IncrementCounter(int By) : ICommand<int>;

public sealed class IncrementCounterHandler(CqrsCounterStore store, IPublisher publisher)
    : ICommandHandler<IncrementCounter, int>
{
    public async Task<int> HandleAsync(IncrementCounter command, CancellationToken cancellationToken)
    {
        var value = store.IncrementBy(command.By);
        await publisher.PublishAsync(new CounterIncremented(value), cancellationToken);
        return value;
    }
}

// --- Notification: fanned out to every handler after the command runs ---
public sealed record CounterIncremented(int Value) : INotification;

public sealed class CounterIncrementedHandler(CqrsCounterStore store) : INotificationHandler<CounterIncremented>
{
    public Task HandleAsync(CounterIncremented notification, CancellationToken cancellationToken)
    {
        store.Note($"🔔 count is now {notification.Value}");
        return Task.CompletedTask;
    }
}

// --- Pipeline behavior (decorator): the extension point for cross-cutting concerns. This one logs
//     every dispatch; a real app would add logging/validation/transactions the same way. Register it
//     with `AddRaskCqrs(o => o.AddOpenBehavior(typeof(DispatchLogBehavior<,>)))`. ---
public sealed class DispatchLogBehavior<TRequest, TResult>(CqrsCounterStore store)
    : IPipelineBehavior<TRequest, TResult>
{
    public Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        store.Note($"⚙ dispatch {typeof(TRequest).Name}");
        return next();
    }
}
